// ─────────────────────────────────────────────────────────────────────────────
// JsonEventSerializer — System.Text.Json-backed IEventSerializer.
//
// Serialization side is easy: hand the object to JsonSerializer, done.
//
// Deserialization needs a type table mapping event_type strings ("poker.card_dealt")
// back to the concrete CLR Type. We build that table once at construction by
// walking every registered module's assembly, finding every IDomainEvent
// implementation, and asking a cheap "uninitialized" instance for its EventType.
//
// Constraint on module-authored IDomainEvent implementations: EventType must
// be a property that returns a constant without touching constructor-injected
// fields. In practice this means
//
//     public sealed record PlayerJoined(...) : IDomainEvent {
//         public string EventType => "poker.player_joined";   // ok
//         public long OccurredAt { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
//     }
//
// A type whose EventType getter depends on ctor params (uncommon) will return
// an empty string through an uninitialized instance and is skipped — the
// serializer logs a warning at startup so the mistake surfaces immediately.
// ─────────────────────────────────────────────────────────────────────────────

using System.Runtime.CompilerServices;
using System.Text.Json;
using BotFramework.Host.Composition;
using BotFramework.Sdk;

namespace BotFramework.Host;

public sealed partial class JsonEventSerializer : IEventSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
    };

    private readonly Dictionary<string, Type> _typesByEventType;
    private readonly ILogger<JsonEventSerializer> _logger;

    public JsonEventSerializer(LoadedModules loadedModules, ILogger<JsonEventSerializer> logger)
    {
        _logger = logger;
        _typesByEventType = BuildTypeTable(loadedModules, logger);
        LogLoadedTypes(_typesByEventType.Count);
    }

    public string Serialize(IDomainEvent ev) => JsonSerializer.Serialize(ev, ev.GetType(), Options);

    public IDomainEvent? Deserialize(string eventType, string payloadJson)
    {
        if (!_typesByEventType.TryGetValue(eventType, out var type))
        {
            LogUnknownEventType(eventType);
            return null;
        }
        return JsonSerializer.Deserialize(payloadJson, type, Options) as IDomainEvent;
    }

    private static Dictionary<string, Type> BuildTypeTable(LoadedModules loadedModules, ILogger logger)
    {
        var table = new Dictionary<string, Type>(StringComparer.Ordinal);
        var eventInterface = typeof(IDomainEvent);
        var seenAssemblies = new HashSet<System.Reflection.Assembly>();

        foreach (var module in loadedModules.Modules)
        {
            var asm = module.GetType().Assembly;
            if (!seenAssemblies.Add(asm)) continue;

            foreach (var type in asm.GetTypes())
            {
                if (type is not { IsClass: true, IsAbstract: false }) continue;
                if (!eventInterface.IsAssignableFrom(type)) continue;

                string? eventTypeName;
                try
                {
                    var instance = (IDomainEvent)RuntimeHelpers.GetUninitializedObject(type);
                    eventTypeName = instance.EventType;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "event.type.probe_failed type={Type}", type.FullName);
                    continue;
                }

                if (string.IsNullOrEmpty(eventTypeName))
                {
                    logger.LogWarning("event.type.empty type={Type}", type.FullName);
                    continue;
                }

                if (table.TryGetValue(eventTypeName, out var existing) && existing != type)
                {
                    throw new InvalidOperationException(
                        $"Duplicate EventType '{eventTypeName}' on {type.FullName} and {existing.FullName}. " +
                        "Event type strings must be unique across all loaded modules.");
                }

                table[eventTypeName] = type;
            }
        }

        return table;
    }

    [LoggerMessage(EventId = 1600, Level = LogLevel.Information, Message = "event.serializer.types count={Count}")]
    partial void LogLoadedTypes(int count);

    [LoggerMessage(EventId = 1601, Level = LogLevel.Warning, Message = "event.deserialize.unknown event_type={EventType}")]
    partial void LogUnknownEventType(string eventType);
}

using System.Text.Json;
using BotFramework.Sdk;
using DotNetCore.CAP;

namespace BotFramework.Host;

public sealed partial class CapEventBus(
    IServiceScopeFactory scopeFactory,
    ILogger<CapEventBus> logger) : IDomainEventBus
{
    internal const string Topic = "domain.event";

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
    private readonly List<Subscription> _subs = [];

    public void Subscribe(string pattern, IDomainEventSubscriber subscriber) =>
        _subs.Add(new(pattern, subscriber));

    public async Task PublishAsync(IDomainEvent ev, CancellationToken ct)
    {
        var envelope = new DomainEventEnvelope(
            ev.EventType,
            ev.GetType().AssemblyQualifiedName!,
            JsonSerializer.Serialize(ev, ev.GetType(), JsonOpts),
            ev.OccurredAt);

        using var scope = scopeFactory.CreateScope();
        var cap = scope.ServiceProvider.GetRequiredService<ICapPublisher>();
        await cap.PublishAsync(Topic, envelope, cancellationToken: ct);
    }

    internal async Task DispatchAsync(DomainEventEnvelope envelope, CancellationToken ct)
    {
        var type = Type.GetType(envelope.TypeName);
        if (type is null)
        {
            LogUnknownType(envelope.TypeName);
            return;
        }

        var ev = (IDomainEvent)JsonSerializer.Deserialize(envelope.Payload, type, JsonOpts)!;

        foreach (var sub in _subs)
        {
            if (!Matches(sub.Pattern, ev.EventType)) continue;
            try
            {
                await sub.Subscriber.HandleAsync(ev, ct);
            }
            catch (Exception ex)
            {
                LogSubscriberFailed(ex, ev.EventType, sub.Subscriber.GetType().Name);
            }
        }
    }

    private static bool Matches(string pattern, string eventType)
    {
        if (pattern == "*") return true;
        if (pattern == eventType) return true;

        var dot = pattern.IndexOf('.');
        if (dot < 0) return false;

        var (patMod, patAction) = (pattern[..dot], pattern[(dot + 1)..]);

        var evDot = eventType.IndexOf('.');
        if (evDot < 0) return false;

        var (evMod, evAction) = (eventType[..evDot], eventType[(evDot + 1)..]);

        return (patMod == "*" || patMod == evMod) && (patAction == "*" || patAction == evAction);
    }

    [LoggerMessage(LogLevel.Error, "event_bus.subscriber_failed event={EventType} subscriber={Subscriber}")]
    partial void LogSubscriberFailed(Exception ex, string eventType, string subscriber);

    [LoggerMessage(LogLevel.Warning, "event_bus.unknown_type typeName={TypeName}")]
    partial void LogUnknownType(string typeName);

    private sealed record Subscription(string Pattern, IDomainEventSubscriber Subscriber);
}

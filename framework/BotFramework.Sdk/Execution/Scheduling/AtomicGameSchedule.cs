using System.Text.Json;
using BotFramework.Contracts.Messaging;
using BotFramework.Contracts.Tenancy;

namespace BotFramework.Sdk.Execution;

public static class AtomicGameSchedule
{
    private const string CommandDataKey = "atomic-command";
    private const string TenantIdDataKey = "__botframework-tenant-id";
    private const string ScopeIdDataKey = "__botframework-scope-id";
    private const string PlayerIdDataKey = "__botframework-player-id";
    private const string RequestIdDataKey = "__botframework-request-id";
    private const string CorrelationIdDataKey = "__botframework-correlation-id";
    private const string ChannelDataKey = "__botframework-channel";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string JobKey<TCommand>()
    {
        return JobKey(typeof(TCommand));
    }

    /// <summary>Returns the scheduler job key for a concrete command type.</summary>
    public static string JobKey(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return $"atomic-game:{type.Assembly.GetName().Name}:{type.FullName ?? type.Name}";
    }

    public static IReadOnlyDictionary<string, string> SerializeCommand<TCommand>(TCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return SerializeCommand(command, typeof(TCommand));
    }

    /// <summary>
    /// Serializes a command whose concrete runtime type is selected by a
    /// transport-neutral helper. The matching scheduler registration still
    /// deserializes it as that exact type.
    /// </summary>
    public static IReadOnlyDictionary<string, string> SerializeCommand(object command, Type commandType)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(commandType);
        if (!commandType.IsInstanceOfType(command))
            throw new ArgumentException("The command must be an instance of its declared command type.", nameof(command));
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [CommandDataKey] = JsonSerializer.Serialize(command, commandType, JsonOptions),
        };
    }

    public static TCommand DeserializeCommand<TCommand>(IReadOnlyDictionary<string, string> data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (!data.TryGetValue(CommandDataKey, out var json) || string.IsNullOrWhiteSpace(json))
            throw new InvalidOperationException("Scheduled atomic command payload is missing.");
        return JsonSerializer.Deserialize<TCommand>(json, JsonOptions)
            ?? throw new InvalidOperationException($"Scheduled atomic command '{typeof(TCommand).Name}' is null.");
    }

    public static IReadOnlyDictionary<string, string> AddTenantContext(
        IReadOnlyDictionary<string, string> data,
        TenantContext context)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(context);
        var result = new Dictionary<string, string>(data, StringComparer.Ordinal)
        {
            [TenantIdDataKey] = context.TenantId.Value,
            [ScopeIdDataKey] = context.ScopeId.Value,
            [RequestIdDataKey] = context.RequestId.Value,
            [CorrelationIdDataKey] = context.CorrelationId.Value,
            [ChannelDataKey] = context.Channel.ToString(),
        };
        if (context.PlayerId is { } player)
            result[PlayerIdDataKey] = player.Value;
        else
            result.Remove(PlayerIdDataKey);
        return result;
    }

    public static bool TryGetTenantContext(
        IReadOnlyDictionary<string, string> data,
        out TenantContext? context)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (!data.TryGetValue(TenantIdDataKey, out var tenantId)
            || !data.TryGetValue(ScopeIdDataKey, out var scopeId)
            || !data.TryGetValue(RequestIdDataKey, out var requestId)
            || !data.TryGetValue(CorrelationIdDataKey, out var correlationId)
            || !data.TryGetValue(ChannelDataKey, out var channelValue)
            || !Enum.TryParse<BotChannel>(channelValue, true, out var channel))
        {
            context = null;
            return false;
        }

        context = TenantContext.Create(
            TenantId.Create(tenantId),
            ScopeId.Create(scopeId),
            data.TryGetValue(PlayerIdDataKey, out var playerId)
                ? PlayerId.Create(playerId)
                : null,
            channel,
            RequestId.Create(requestId),
            RequestId.Create(correlationId));
        return true;
    }
}

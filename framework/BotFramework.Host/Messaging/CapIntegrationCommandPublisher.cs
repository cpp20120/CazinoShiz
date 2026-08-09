using System.Text.Json;
using BotFramework.Contracts.Messaging;
using BotFramework.Contracts.Tenancy;
using DotNetCore.CAP;

namespace BotFramework.Host.Messaging;

public sealed class CapIntegrationCommandPublisher(
    IIntegrationOutbox outbox,
    IIntegrationMessageRouter router,
    IIntegrationInboxContextAccessor inboxContext,
    ITenantContextAccessor tenantContext,
    IntegrationInboxOptions options) : IIntegrationCommandPublisher
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task SendAsync<TCommand>(TCommand command, CancellationToken ct)
        where TCommand : IIntegrationCommand
        => SendCoreAsync(command, ct);

    public Task SendAsync(IIntegrationCommand command, CancellationToken ct) =>
        SendCoreAsync(command, ct);

    private Task SendCoreAsync(IIntegrationCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        var metadata = RequestMetadataContext.TryGetCurrent();
        var context = tenantContext.Current ?? metadata?.TenantContext;
        var messageId = Guid.NewGuid().ToString("N");
        var correlationId = context?.CorrelationId.ToString()
            ?? metadata?.CorrelationId
            ?? messageId;
        var causationId = metadata?.RequestId ?? messageId;
        var tenantId = context?.TenantId.ToString() ?? metadata?.Tenant?.ToString();
        var scopeId = context?.ScopeId.ToString() ?? metadata?.TypedScope?.ToString();
        var playerId = context?.PlayerId?.ToString() ?? metadata?.Player?.ToString();
        var route = router.Route(
            IntegrationMessageKind.Command,
            command.CommandType,
            command,
            tenantId,
            scopeId,
            playerId);
        var payload = JsonSerializer.Serialize(command, command.GetType(), JsonOptions);

        var envelope = new IntegrationCommandEnvelope(
            messageId,
            command.CommandType,
            IntegrationContractNames.Stable(command.GetType()),
            SchemaVersion: 1,
            payload,
            command.OccurredAt,
            correlationId,
            causationId,
            tenantId,
            scopeId,
            playerId,
            context?.Channel ?? metadata?.Channel ?? BotChannel.System,
            route.Topic,
            route.MessageKey);

        return outbox.EnqueueAsync(
            new IntegrationOutboxMessage(
                options.ConsumerName,
                messageId,
                IntegrationMessageKind.Command,
                route.Topic,
                route.MessageKey,
                command.CommandType,
                envelope.ContractType,
                envelope.SchemaVersion,
                payload,
                JsonSerializer.Serialize(envelope, JsonOptions),
                envelope.OccurredAt,
                envelope.CorrelationId,
                envelope.CausationId,
                envelope.TenantId,
                envelope.ScopeId,
                envelope.PlayerId,
                envelope.Channel),
            inboxContext.Current is { } current
                ? IntegrationTransactionContext.From(current)
                : null,
            ct);
    }
}

using System.Text.Json;
using BotFramework.Contracts.Messaging;
using BotFramework.Host.Wagering;
using Microsoft.Extensions.DependencyInjection;

namespace BotFramework.Host.Execution;

internal sealed partial class GameEventOutboxDispatcher(
    PostgresGameEventOutbox outbox,
    IDomainEventBus events,
    IServiceScopeFactory scopeFactory,
    ILogger<GameEventOutboxDispatcher> logger,
    TimeProvider timeProvider) : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
        do
        {
            try
            {
                await DispatchBatchAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogPollFailed(logger, exception);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task DispatchBatchAsync(CancellationToken ct)
    {
        var batch = await outbox.ClaimAsync(50, TimeSpan.FromMinutes(1), ct);
        foreach (var item in batch)
        {
            try
            {
                var type = Type.GetType(item.TypeName, throwOnError: true)!;
                var domainEvent = JsonSerializer.Deserialize(item.Payload, type, JsonOptions) as IDomainEvent
                    ?? throw new InvalidOperationException($"Outbox payload is not an {nameof(IDomainEvent)}.");
                await RelayWagerOutcomeAsync(domainEvent, ct);
                await events.PublishAsync(domainEvent, ct);
                await outbox.MarkSentAsync(item.Id, ct);
                GameExecutionTelemetry.RecordOutboxLag(item.CreatedAt, timeProvider.GetUtcNow());
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogDeliveryFailed(logger, exception, item.Id, item.Attempts);
                await outbox.MarkFailedAsync(item.Id, exception.Message, item.Attempts, ct);
            }
        }

        var tenantBatch = await outbox.ClaimTenantAsync(50, TimeSpan.FromMinutes(1), ct);
        foreach (var item in tenantBatch)
        {
            try
            {
                var type = Type.GetType(item.TypeName, throwOnError: true)!;
                var domainEvent = JsonSerializer.Deserialize(item.Payload, type, JsonOptions) as IDomainEvent
                    ?? throw new InvalidOperationException($"Tenant outbox payload is not an {nameof(IDomainEvent)}.");
                using var metadataScope = RequestMetadataContext.Push(
                    RequestMetadata.FromTenantContext(item.Context, "tenant-event-outbox"));
                await RelayWagerOutcomeAsync(domainEvent, ct);
                await events.PublishAsync(domainEvent, ct);
                await outbox.MarkTenantSentAsync(item.Id, ct);
                GameExecutionTelemetry.RecordOutboxLag(item.CreatedAt, timeProvider.GetUtcNow());
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogDeliveryFailed(logger, exception, item.Id, item.Attempts);
                await outbox.MarkTenantFailedAsync(item.Id, exception.Message, item.Attempts, ct);
            }
        }
    }

    private async Task RelayWagerOutcomeAsync(IDomainEvent domainEvent, CancellationToken ct)
    {
        if (domainEvent is not WagerGameOutcomeDeclared outcome)
            return;

        using var scope = scopeFactory.CreateScope();
        await scope.ServiceProvider
            .GetRequiredService<WagerGameOutcomeIntegrationBridge>()
            .HandleAsync(outcome, ct);
    }

    [LoggerMessage(LogLevel.Warning, "game.event.outbox.delivery_failed id={OutboxId} attempts={Attempts}")]
    private static partial void LogDeliveryFailed(ILogger logger, Exception exception, long outboxId, int attempts);

    [LoggerMessage(LogLevel.Warning, "game.event.outbox.poll_failed")]
    private static partial void LogPollFailed(ILogger logger, Exception exception);
}

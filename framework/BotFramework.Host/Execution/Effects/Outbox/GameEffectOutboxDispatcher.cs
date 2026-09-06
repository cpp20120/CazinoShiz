using System.Text.Json;
using BotFramework.Sdk.Execution;
using Microsoft.Extensions.DependencyInjection;

namespace BotFramework.Host.Execution;

internal sealed partial class GameEffectOutboxDispatcher(
    PostgresGameEffectOutbox outbox,
    IServiceScopeFactory scopeFactory,
    ILogger<GameEffectOutboxDispatcher> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
        do
        {
            try
            {
                foreach (var item in await outbox.ClaimAsync(50, TimeSpan.FromMinutes(1), stoppingToken))
                    await DeliverAsync(item, stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogPollFailed(logger, exception);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task DeliverAsync(GameEffectOutboxItem item, CancellationToken ct)
    {
        try
        {
            var type = Type.GetType(item.TypeName, throwOnError: true)!;
            var effect = JsonSerializer.Deserialize(item.Payload, type, JsonOptions) as IDurableGameEffect
                ?? throw new InvalidOperationException($"Outbox payload is not an {nameof(IDurableGameEffect)}.");
            using var scope = scopeFactory.CreateScope();
            var handler = ResolveHandler(effect.GetType(), scope.ServiceProvider.GetServices<IGameEffectHandler>());
            await handler.ApplyAsync([effect], new OutboxGameExecutionContext(item), ct);
            await outbox.MarkSentAsync(item.Id, ct);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogDeliveryFailed(logger, exception, item.Id, item.Attempts);
            await outbox.MarkFailedAsync(item.Id, exception.Message, item.Attempts, ct);
        }
    }

    private static IGameEffectHandler ResolveHandler(
        Type effectType,
        IEnumerable<IGameEffectHandler> handlers)
    {
        var registered = handlers.ToArray();
        var exact = registered.Where(handler => handler.EffectType == effectType).ToArray();
        if (exact.Length == 1)
            return exact[0];
        if (exact.Length > 1)
            throw new InvalidOperationException($"Multiple game effect handlers are registered for '{effectType}'.");

        var compatible = registered
            .Where(handler => handler.EffectType.IsAssignableFrom(effectType))
            .ToArray();
        return compatible.Length switch
        {
            0 => throw new InvalidOperationException($"No game effect handler is registered for '{effectType}'."),
            1 => compatible[0],
            _ => throw new InvalidOperationException(
                $"More than one compatible game effect handler is registered for '{effectType}'."),
        };
    }

    [LoggerMessage(LogLevel.Warning, "game.effect.outbox.delivery_failed id={OutboxId} attempts={Attempts}")]
    private static partial void LogDeliveryFailed(ILogger logger, Exception exception, long outboxId, int attempts);

    [LoggerMessage(LogLevel.Warning, "game.effect.outbox.poll_failed")]
    private static partial void LogPollFailed(ILogger logger, Exception exception);
}

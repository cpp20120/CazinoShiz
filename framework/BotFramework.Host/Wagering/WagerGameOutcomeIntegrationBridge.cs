using BotFramework.Contracts.Messaging;
using BotFramework.Contracts.Wagering;

namespace BotFramework.Host.Wagering;

public sealed class WagerGameOutcomeIntegrationBridge(IServiceProvider services)
{
    public async Task HandleAsync(WagerGameOutcomeDeclared outcome, CancellationToken ct)
    {
        using var scope = services.CreateScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IIntegrationEventPublisher>();
        await publisher.PublishAsync(
            new GameOutcomeDeclared(
                outcome.BetId,
                outcome.GameId,
                outcome.PlayerId,
                outcome.OutcomeCode,
                outcome.Evidence,
                DateTimeOffset.FromUnixTimeMilliseconds(outcome.OccurredAt)),
            ct);
    }
}

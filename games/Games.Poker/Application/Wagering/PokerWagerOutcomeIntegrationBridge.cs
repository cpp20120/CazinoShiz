using BotFramework.Contracts.Messaging;
using BotFramework.Contracts.Wagering;
using BotFramework.Sdk.Events.Bus;
using Games.Poker.Domain.Events;
using Microsoft.Extensions.DependencyInjection;

namespace Games.Poker.Application.Wagering;

public sealed class PokerWagerOutcomeIntegrationBridge(IServiceProvider services)
    : IDomainEventSubscriber
{
    public async Task HandleAsync(IDomainEvent ev, CancellationToken ct)
    {
        if (ev is not PokerWagerOutcomeDeclared outcome) return;

        await using var scope = services.CreateAsyncScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IIntegrationEventPublisher>();
        await publisher.PublishAsync(
            new GameOutcomeDeclared(
                outcome.BetId,
                "poker",
                outcome.PlayerId,
                outcome.OutcomeCode,
                outcome.Evidence,
                DateTimeOffset.FromUnixTimeMilliseconds(outcome.OccurredAt)),
            ct);
    }
}

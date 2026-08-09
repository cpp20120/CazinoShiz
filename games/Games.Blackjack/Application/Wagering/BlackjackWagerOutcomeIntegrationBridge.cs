using BotFramework.Contracts.Messaging;
using BotFramework.Contracts.Wagering;
using BotFramework.Sdk.Events.Bus;
using Microsoft.Extensions.DependencyInjection;
using Games.Blackjack.Domain.Events;

namespace Games.Blackjack.Application.Wagering;

/// <summary>
/// Relays the committed game event into the integration outbox. The game
/// transaction never calls the broker and cannot expose an uncommitted outcome.
/// </summary>
public sealed class BlackjackWagerOutcomeIntegrationBridge(IServiceProvider services)
    : IDomainEventSubscriber
{
    public async Task HandleAsync(IDomainEvent ev, CancellationToken ct)
    {
        if (ev is not BlackjackWagerOutcomeDeclared outcome) return;

        await using var scope = services.CreateAsyncScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IIntegrationEventPublisher>();
        await publisher.PublishAsync(
            new GameOutcomeDeclared(
                outcome.BetId,
                "blackjack",
                outcome.PlayerId,
                outcome.OutcomeCode,
                outcome.Evidence,
                DateTimeOffset.FromUnixTimeMilliseconds(outcome.OccurredAt)),
            ct);
    }
}

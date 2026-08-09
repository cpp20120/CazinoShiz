using BotFramework.Contracts.Messaging;
using BotFramework.Contracts.Wagering;
using BotFramework.Sdk.Events.Bus;
using Games.Horse.Domain.Events;
using Microsoft.Extensions.DependencyInjection;

namespace Games.Horse.Application.Wagering;

public sealed class HorseWagerOutcomeIntegrationBridge(IServiceProvider services)
    : IDomainEventSubscriber
{
    public async Task HandleAsync(IDomainEvent ev, CancellationToken ct)
    {
        if (ev is not HorseWagerOutcomeDeclared outcome) return;
        await using var scope = services.CreateAsyncScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IIntegrationEventPublisher>();
        await publisher.PublishAsync(new GameOutcomeDeclared(
            outcome.BetId, "horse", outcome.PlayerId, outcome.OutcomeCode,
            outcome.Evidence, DateTimeOffset.FromUnixTimeMilliseconds(outcome.OccurredAt)), ct);
    }
}

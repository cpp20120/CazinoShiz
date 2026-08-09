using BotFramework.Contracts.Messaging;
using BotFramework.Contracts.Wagering;
using BotFramework.Sdk.Events.Bus;
using Microsoft.Extensions.DependencyInjection;
using Games.SecretHitler.Domain.Events;

namespace Games.SecretHitler.Application.Wagering;

public sealed class SecretHitlerWagerOutcomeIntegrationBridge(IServiceProvider services)
    : IDomainEventSubscriber
{
    public async Task HandleAsync(IDomainEvent ev, CancellationToken ct)
    {
        if (ev is not SecretHitlerWagerOutcomeDeclared outcome) return;
        await using var scope = services.CreateAsyncScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IIntegrationEventPublisher>();
        await publisher.PublishAsync(new GameOutcomeDeclared(
            outcome.BetId, "sh", outcome.PlayerId, outcome.OutcomeCode,
            outcome.Evidence, DateTimeOffset.FromUnixTimeMilliseconds(outcome.OccurredAt)), ct);
    }
}

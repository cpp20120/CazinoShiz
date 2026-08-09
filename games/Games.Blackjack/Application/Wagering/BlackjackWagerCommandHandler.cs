using BotFramework.Contracts.Messaging;
using BotFramework.Host.Execution;
using Games.Blackjack.Application.Execution;
using Games.Blackjack.Contracts.Domain.Results;
using Games.Blackjack.Contracts.Integration;

namespace Games.Blackjack.Application.Wagering;

/// <summary>Integration-command adapter for the outcome-only blackjack aggregate.</summary>
public sealed class BlackjackWagerCommandHandler(
    IOutcomeOnlyGameExecutor<BlackjackWagerStart, BlackjackWagerState, BlackjackWagerResult> start,
    IOutcomeOnlyGameExecutor<BlackjackWagerHit, BlackjackWagerState, BlackjackWagerResult> hit,
    IOutcomeOnlyGameExecutor<BlackjackWagerStand, BlackjackWagerState, BlackjackWagerResult> stand,
    IOutcomeOnlyGameExecutor<BlackjackWagerTimeout, BlackjackWagerState, BlackjackWagerResult> timeout)
    : IIntegrationCommandHandler<BlackjackWagerStart>,
      IIntegrationCommandHandler<BlackjackWagerHit>,
      IIntegrationCommandHandler<BlackjackWagerStand>,
      IIntegrationCommandHandler<BlackjackWagerTimeout>
{
    public Task HandleAsync(BlackjackWagerStart command, CancellationToken ct) =>
        start.ExecuteAsync(new GameExecutionEnvelope<BlackjackWagerStart>(command), ct);

    public Task HandleAsync(BlackjackWagerHit command, CancellationToken ct) =>
        hit.ExecuteAsync(new GameExecutionEnvelope<BlackjackWagerHit>(command), ct);

    public Task HandleAsync(BlackjackWagerStand command, CancellationToken ct) =>
        stand.ExecuteAsync(new GameExecutionEnvelope<BlackjackWagerStand>(command), ct);

    public Task HandleAsync(BlackjackWagerTimeout command, CancellationToken ct) =>
        timeout.ExecuteAsync(new GameExecutionEnvelope<BlackjackWagerTimeout>(command), ct);
}

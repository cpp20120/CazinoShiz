using BotFramework.Sdk.Execution;
using Games.Blackjack.Contracts.Domain.Results;
using Games.Blackjack.Domain.Events;
using Games.Blackjack.Domain.Results;

namespace Games.Blackjack.Application.Execution;

internal static class BlackjackWagerActionSupport
{
    public const string TimeoutScheduleId = "wager-timeout";

    public static GameDecision<BlackjackWagerState, BlackjackWagerResult> Reject(
        BlackjackWagerState state,
        BlackjackError error,
        string reason) =>
        new(
            DecisionStatus.Rejected,
            state,
            new BlackjackWagerResult(error, state.Outcome is null ? null : BlackjackWagerRules.BuildSnapshot(state)),
            [],
            [],
            [],
            [],
            [],
            reason);

    public static GameDecision<BlackjackWagerState, BlackjackWagerResult> Complete<TCommand>(
        GameActionInput<BlackjackWagerState, TCommand> input,
        BlackjackWagerState state,
        string deckState,
        string[] dealerCards,
        BlackjackWagerRules.Resolution resolution,
        bool cancelTimeout = true)
    {
        var completed = state with
        {
            Revision = checked(state.Revision + 1),
            Status = TurnGameStatus.Completed,
            TurnDeadline = null,
            DealerCards = dealerCards,
            DeckState = deckState,
            Outcome = resolution.Outcome,
            OutcomeEvidence = resolution.Evidence,
        };
        var occurredAt = input.UtcNow.ToUnixTimeMilliseconds();
        var outcome = new BlackjackWagerOutcomeDeclared(
            completed.BetId,
            completed.PlayerId,
            resolution.Outcome.ToString(),
            resolution.Evidence,
            completed.Revision,
            occurredAt);
        return new GameDecision<BlackjackWagerState, BlackjackWagerResult>(
            DecisionStatus.Accepted,
            completed,
            new BlackjackWagerResult(
                BlackjackError.None,
                BlackjackWagerRules.BuildSnapshot(completed),
                completed.StateMessageId),
            [],
            [],
            [],
            [outcome],
            cancelTimeout ? [ScheduleEffect.Cancel(TimeoutScheduleId)] : []);
    }
}

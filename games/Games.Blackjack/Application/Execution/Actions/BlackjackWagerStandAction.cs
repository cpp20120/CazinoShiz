using BotFramework.Sdk.Execution;
using Games.Blackjack.Contracts.Domain.Results;
using Games.Blackjack.Contracts.Integration;
using Games.Blackjack.Domain.Results;

namespace Games.Blackjack.Application.Execution;

public sealed class BlackjackWagerStandAction
    : IGameAction<BlackjackWagerStand, BlackjackWagerState, BlackjackWagerResult>
{
    public GameDecision<BlackjackWagerState, BlackjackWagerResult> Decide(
        GameActionInput<BlackjackWagerState, BlackjackWagerStand> input)
    {
        if (input.Command.ExpectedRevision != input.State.Revision)
            return BlackjackWagerActionSupport.Reject(input.State, BlackjackError.NoActiveHand, "stale_revision");
        if (input.State.Status != TurnGameStatus.Active
            || !string.Equals(input.State.PlayerId, input.Command.PlayerId, StringComparison.Ordinal))
        {
            return BlackjackWagerActionSupport.Reject(input.State, BlackjackError.NoActiveHand, "hand_not_active");
        }
        if (input.State.TurnDeadline is { } deadline && input.UtcNow >= deadline)
            return BlackjackWagerActionSupport.Reject(input.State, BlackjackError.NoActiveHand, "turn_expired");

        var resolution = BlackjackWagerRules.Resolve(
            input.State.PlayerCards,
            input.State.DealerCards,
            input.State.DeckState);
        return BlackjackWagerActionSupport.Complete(
            input,
            input.State,
            resolution.DeckState,
            resolution.DealerCards,
            resolution);
    }
}

using BotFramework.Sdk.Execution;
using Games.Blackjack.Contracts.Domain.Results;
using Games.Blackjack.Contracts.Integration;

namespace Games.Blackjack.Application.Execution;

public sealed class BlackjackWagerTimeoutAction
    : IGameAction<BlackjackWagerTimeout, BlackjackWagerState, BlackjackWagerResult>
{
    public GameDecision<BlackjackWagerState, BlackjackWagerResult> Decide(
        GameActionInput<BlackjackWagerState, BlackjackWagerTimeout> input)
    {
        if (input.State.Status != TurnGameStatus.Active
            || !string.Equals(input.State.BetId, input.Command.BetId, StringComparison.Ordinal)
            || !string.Equals(input.State.PlayerId, input.Command.PlayerId, StringComparison.Ordinal))
        {
            return BlackjackWagerActionSupport.Reject(input.State, BlackjackError.NoActiveHand, "hand_not_active");
        }
        if (input.State.TurnDeadline is not { } deadline || input.UtcNow < deadline)
            throw new InvalidOperationException("Blackjack wager timeout fired before its deadline.");

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

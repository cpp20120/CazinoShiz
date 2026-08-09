using BotFramework.Sdk.Execution;
using Games.Blackjack.Contracts.Domain.Results;
using Games.Blackjack.Contracts.Integration;
using Games.Blackjack.Domain.Models;
using Games.Blackjack.Domain.Results;

namespace Games.Blackjack.Application.Execution;

public sealed class BlackjackWagerHitAction
    : IGameAction<BlackjackWagerHit, BlackjackWagerState, BlackjackWagerResult>
{
    public GameDecision<BlackjackWagerState, BlackjackWagerResult> Decide(
        GameActionInput<BlackjackWagerState, BlackjackWagerHit> input)
    {
        var validation = ValidateTurn(input.State, input.Command, input.UtcNow);
        if (validation is not null) return validation;

        var deck = input.State.DeckState;
        var card = Deck.Draw(ref deck, 1)[0];
        var playerCards = input.State.PlayerCards.Append(card).ToArray();
        var advanced = input.State with
        {
            PlayerCards = playerCards,
            DeckState = deck,
        };
        if (BlackjackHandValue.Compute(playerCards) > 21)
        {
            var resolution = BlackjackWagerRules.Resolve(playerCards, advanced.DealerCards, deck);
            return BlackjackWagerActionSupport.Complete(
                input,
                advanced,
                resolution.DeckState,
                resolution.DealerCards,
                resolution);
        }

        return new GameDecision<BlackjackWagerState, BlackjackWagerResult>(
            DecisionStatus.Accepted,
            advanced with { Revision = checked(input.State.Revision + 1) },
            new BlackjackWagerResult(
                BlackjackError.None,
                BlackjackWagerRules.BuildSnapshot(advanced with { Revision = checked(input.State.Revision + 1) }),
                advanced.StateMessageId),
            [],
            [],
            [],
            [],
            []);
    }

    private static GameDecision<BlackjackWagerState, BlackjackWagerResult>? ValidateTurn(
        BlackjackWagerState state,
        BlackjackWagerHit command,
        DateTimeOffset utcNow)
    {
        if (command.ExpectedRevision != state.Revision)
            return BlackjackWagerActionSupport.Reject(state, BlackjackError.NoActiveHand, "stale_revision");
        if (state.Status != TurnGameStatus.Active
            || !string.Equals(state.PlayerId, command.PlayerId, StringComparison.Ordinal))
        {
            return BlackjackWagerActionSupport.Reject(state, BlackjackError.NoActiveHand, "hand_not_active");
        }
        if (state.TurnDeadline is { } deadline && utcNow >= deadline)
            return BlackjackWagerActionSupport.Reject(state, BlackjackError.NoActiveHand, "turn_expired");
        return null;
    }
}

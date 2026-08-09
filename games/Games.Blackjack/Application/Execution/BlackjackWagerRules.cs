using System.Text.Json;
using BotFramework.Sdk.Execution;
using Games.Blackjack.Contracts.Domain.Results;
using Games.Blackjack.Domain.Models;
using Games.Blackjack.Domain.Results;

namespace Games.Blackjack.Application.Execution;

internal static class BlackjackWagerRules
{
    public static BlackjackWagerSnapshot BuildSnapshot(BlackjackWagerState state)
    {
        var revealed = state.Outcome.HasValue;
        var dealerCards = revealed ? state.DealerCards : state.DealerCards.Take(1).ToArray();
        return new BlackjackWagerSnapshot(
            state.BetId,
            state.PlayerCards,
            dealerCards,
            BlackjackHandValue.Compute(state.PlayerCards),
            BlackjackHandValue.Compute(dealerCards),
            revealed,
            !revealed && state.Status == TurnGameStatus.Active,
            state.Outcome,
            state.StateMessageId);
    }

    public static Resolution Resolve(
        IReadOnlyList<string> playerCards,
        IReadOnlyList<string> dealerCards,
        string deckState)
    {
        var player = playerCards.ToArray();
        var dealer = dealerCards.ToList();
        var deck = deckState;
        var playerTotal = BlackjackHandValue.Compute(player);
        var playerBlackjack = BlackjackHandValue.IsNaturalBlackjack(player);

        if (playerTotal <= 21)
        {
            while (BlackjackHandValue.Compute(dealer) < 17)
                dealer.Add(Deck.Draw(ref deck, 1)[0]);
        }

        var dealerTotal = BlackjackHandValue.Compute(dealer);
        var dealerBlackjack = BlackjackHandValue.IsNaturalBlackjack(dealer);
        var outcome = ResolveOutcome(playerTotal, dealerTotal, playerBlackjack, dealerBlackjack);
        var evidence = JsonSerializer.Serialize(new
        {
            playerTotal,
            dealerTotal,
            playerCards = player,
            dealerCards = dealer.ToArray(),
        });

        return new Resolution(outcome, dealer.ToArray(), deck, evidence);
    }

    private static BlackjackOutcome ResolveOutcome(
        int playerTotal,
        int dealerTotal,
        bool playerBlackjack,
        bool dealerBlackjack)
    {
        if (playerTotal > 21) return BlackjackOutcome.PlayerBust;
        if (playerBlackjack && !dealerBlackjack) return BlackjackOutcome.PlayerBlackjack;
        if (playerBlackjack && dealerBlackjack) return BlackjackOutcome.Push;
        if (dealerTotal > 21) return BlackjackOutcome.DealerBust;
        if (playerTotal > dealerTotal) return BlackjackOutcome.PlayerWin;
        if (playerTotal < dealerTotal) return BlackjackOutcome.DealerWin;
        return BlackjackOutcome.Push;
    }

    internal sealed record Resolution(
        BlackjackOutcome Outcome,
        string[] DealerCards,
        string DeckState,
        string Evidence);
}

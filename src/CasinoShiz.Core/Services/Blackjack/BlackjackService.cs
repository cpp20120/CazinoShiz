using CasinoShiz.Configuration;
using CasinoShiz.Data;
using CasinoShiz.Data.Entities;
using CasinoShiz.Services.Analytics;
using CasinoShiz.Services.Economics;
using CasinoShiz.Services.Poker.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CasinoShiz.Services.Blackjack;

public sealed class BlackjackService(
    AppDbContext db,
    IOptions<BotOptions> options,
    ClickHouseReporter reporter,
    EconomicsService economics)
{
    private readonly BotOptions _opts = options.Value;

    public async Task<BlackjackResult> StartAsync(
        long userId, string displayName, long chatId, int bet, CancellationToken ct)
    {
        if (bet < _opts.BlackjackMinBet || bet > _opts.BlackjackMaxBet)
            return new BlackjackResult(BlackjackError.InvalidBet, null);

        var existing = await db.BlackjackHands.FindAsync([userId], ct);
        if (existing != null)
            return new BlackjackResult(BlackjackError.HandInProgress, null);

        var user = await EnsureUserAsync(userId, displayName, ct);
        if (user.Coins < bet)
        {
            reporter.SendEvent(new EventData
            {
                EventType = "blackjack",
                Payload = new { type = "not_enough_coins", user_id = userId, chat_id = chatId, bet },
            });
            return new BlackjackResult(BlackjackError.NotEnoughCoins, null);
        }

        await economics.DebitAsync(user, bet, "blackjack.start", ct);

        var deck = Deck.BuildShuffled();
        var player = Deck.Draw(ref deck, 2);
        var dealer = Deck.Draw(ref deck, 2);

        var hand = new BlackjackHand
        {
            UserId = userId,
            Bet = bet,
            PlayerCards = string.Join(" ", player),
            DealerCards = string.Join(" ", dealer),
            DeckState = deck,
            ChatId = chatId,
            CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };
        db.BlackjackHands.Add(hand);

        reporter.SendEvent(new EventData
        {
            EventType = "blackjack",
            Payload = new { type = "start", user_id = userId, chat_id = chatId, bet },
        });

        if (BlackjackHandValue.IsNaturalBlackjack(player))
        {
            return await SettleAsync(hand, user, doubled: false, ct);
        }

        await db.SaveChangesAsync(ct);
        return new BlackjackResult(BlackjackError.None, BuildSnapshot(hand, user, revealed: false), hand.StateMessageId);
    }

    public async Task<BlackjackResult> HitAsync(long userId, CancellationToken ct)
    {
        var hand = await db.BlackjackHands.FindAsync([userId], ct);
        if (hand == null) return new BlackjackResult(BlackjackError.NoActiveHand, null);
        var user = await db.Users.FindAsync([userId], ct);
        if (user == null) return new BlackjackResult(BlackjackError.NoActiveHand, null);

        var deck = hand.DeckState;
        var drawn = Deck.Draw(ref deck, 1);
        var player = Deck.Parse(hand.PlayerCards).Append(drawn[0]).ToArray();
        hand.PlayerCards = string.Join(" ", player);
        hand.DeckState = deck;

        if (BlackjackHandValue.Compute(player) > 21)
            return await SettleAsync(hand, user, doubled: false, ct);

        await db.SaveChangesAsync(ct);
        return new BlackjackResult(BlackjackError.None, BuildSnapshot(hand, user, revealed: false), hand.StateMessageId);
    }

    public async Task<BlackjackResult> StandAsync(long userId, CancellationToken ct)
    {
        var hand = await db.BlackjackHands.FindAsync([userId], ct);
        if (hand == null) return new BlackjackResult(BlackjackError.NoActiveHand, null);
        var user = await db.Users.FindAsync([userId], ct);
        if (user == null) return new BlackjackResult(BlackjackError.NoActiveHand, null);

        return await SettleAsync(hand, user, doubled: false, ct);
    }

    public async Task<BlackjackResult> DoubleAsync(long userId, CancellationToken ct)
    {
        var hand = await db.BlackjackHands.FindAsync([userId], ct);
        if (hand == null) return new BlackjackResult(BlackjackError.NoActiveHand, null);
        var user = await db.Users.FindAsync([userId], ct);
        if (user == null) return new BlackjackResult(BlackjackError.NoActiveHand, null);

        var player = Deck.Parse(hand.PlayerCards);
        if (player.Length != 2) return new BlackjackResult(BlackjackError.CannotDouble, null);
        if (user.Coins < hand.Bet) return new BlackjackResult(BlackjackError.NotEnoughCoins, null);

        await economics.DebitAsync(user, hand.Bet, "blackjack.double", ct);
        hand.Bet *= 2;

        var deck = hand.DeckState;
        var drawn = Deck.Draw(ref deck, 1);
        hand.PlayerCards = string.Join(" ", player.Append(drawn[0]));
        hand.DeckState = deck;

        return await SettleAsync(hand, user, doubled: true, ct);
    }

    public async Task<(BlackjackSnapshot? snapshot, int? stateMessageId)> GetSnapshotAsync(long userId, CancellationToken ct)
    {
        var hand = await db.BlackjackHands.FindAsync([userId], ct);
        if (hand == null) return (null, null);
        var user = await db.Users.FindAsync([userId], ct);
        if (user == null) return (null, null);
        return (BuildSnapshot(hand, user, revealed: false), hand.StateMessageId);
    }

    public async Task SetStateMessageIdAsync(long userId, int messageId, CancellationToken ct)
    {
        var hand = await db.BlackjackHands.FindAsync([userId], ct);
        if (hand == null) return;
        hand.StateMessageId = messageId;
        await db.SaveChangesAsync(ct);
    }

    private async Task<BlackjackResult> SettleAsync(
        BlackjackHand hand, UserState user, bool doubled, CancellationToken ct)
    {
        var player = Deck.Parse(hand.PlayerCards);
        var dealer = Deck.Parse(hand.DealerCards).ToList();
        var deck = hand.DeckState;

        var playerTotal = BlackjackHandValue.Compute(player);
        var playerBj = !doubled && BlackjackHandValue.IsNaturalBlackjack(player);

        if (playerTotal <= 21)
        {
            while (BlackjackHandValue.Compute(dealer) < 17)
            {
                var drawn = Deck.Draw(ref deck, 1);
                dealer.Add(drawn[0]);
            }
        }

        var dealerTotal = BlackjackHandValue.Compute(dealer);
        var dealerBj = BlackjackHandValue.IsNaturalBlackjack(dealer);

        var (outcome, payout) = Resolve(playerTotal, dealerTotal, playerBj, dealerBj, hand.Bet);
        await economics.CreditAsync(user, payout, "blackjack.settle", ct);
        user.BlackjackHandsPlayed++;

        hand.DealerCards = string.Join(" ", dealer);
        hand.DeckState = deck;

        var stateMessageId = hand.StateMessageId;
        var chatId = hand.ChatId;
        var settledBet = hand.Bet;
        db.BlackjackHands.Remove(hand);
        await db.SaveChangesAsync(ct);

        reporter.SendEvent(new EventData
        {
            EventType = "blackjack",
            Payload = new
            {
                type = "end",
                user_id = user.TelegramUserId,
                chat_id = chatId,
                bet = settledBet,
                payout,
                player_total = playerTotal,
                dealer_total = dealerTotal,
                outcome = outcome.ToString(),
                doubled,
            },
        });

        var snapshot = new BlackjackSnapshot(
            player, [.. dealer], playerTotal, dealerTotal,
            settledBet, user.Coins,
            DealerHoleRevealed: true, CanDouble: false,
            Outcome: outcome, Payout: payout);
        return new BlackjackResult(BlackjackError.None, snapshot, stateMessageId);
    }

    private static (BlackjackOutcome outcome, int payout) Resolve(
        int playerTotal, int dealerTotal, bool playerBj, bool dealerBj, int bet)
    {
        if (playerTotal > 21) return (BlackjackOutcome.PlayerBust, 0);
        if (playerBj && !dealerBj) return (BlackjackOutcome.PlayerBlackjack, bet + (bet * 3 / 2));
        if (playerBj && dealerBj) return (BlackjackOutcome.Push, bet);
        if (dealerTotal > 21) return (BlackjackOutcome.DealerBust, bet * 2);
        if (playerTotal > dealerTotal) return (BlackjackOutcome.PlayerWin, bet * 2);
        if (playerTotal < dealerTotal) return (BlackjackOutcome.DealerWin, 0);
        return (BlackjackOutcome.Push, bet);
    }

    private static BlackjackSnapshot BuildSnapshot(BlackjackHand hand, UserState user, bool revealed)
    {
        var player = Deck.Parse(hand.PlayerCards);
        var dealer = Deck.Parse(hand.DealerCards);
        var playerTotal = BlackjackHandValue.Compute(player);
        var dealerVisible = revealed ? dealer : dealer.Take(1).ToArray();
        var dealerTotal = BlackjackHandValue.Compute(dealerVisible);
        var canDouble = player.Length == 2 && user.Coins >= hand.Bet;
        return new BlackjackSnapshot(
            player, dealerVisible, playerTotal, dealerTotal,
            hand.Bet, user.Coins, revealed, canDouble,
            Outcome: null, Payout: 0);
    }

    private async Task<UserState> EnsureUserAsync(long userId, string displayName, CancellationToken ct)
    {
        var user = await db.Users.FindAsync([userId], ct);
        if (user != null) return user;
        user = new UserState { TelegramUserId = userId, DisplayName = displayName, Coins = 100 };
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        return user;
    }
}

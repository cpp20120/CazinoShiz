// ─────────────────────────────────────────────────────────────────────────────
// BlackjackService — application service for the /blackjack game.
//
// Port of src/CasinoShiz.Core/Services/Blackjack/BlackjackService.cs:
//   • EF Core BlackjackHand row → BlackjackHandStore (Dapper).
//   • EconomicsService.Debit/Credit calls now take userId (not entity).
//   • Natural-blackjack at Start settles immediately without persisting the
//     hand (identical to the monolith's fast-path).
//
// Lifecycle: Start → [loop: Hit / Stand / Double] → Settle. Settle deletes the
// hand row, credits any payout, and publishes BlackjackHandCompleted.
// ─────────────────────────────────────────────────────────────────────────────

using BotFramework.Host;
using BotFramework.Sdk;
using Games.Blackjack.Domain;
using Microsoft.Extensions.Options;

namespace Games.Blackjack;

public interface IBlackjackService
{
    Task<BlackjackResult> StartAsync(long userId, string displayName, long chatId, int bet, CancellationToken ct);
    Task<BlackjackResult> HitAsync(long userId, CancellationToken ct);
    Task<BlackjackResult> StandAsync(long userId, CancellationToken ct);
    Task<BlackjackResult> DoubleAsync(long userId, CancellationToken ct);
    Task<(BlackjackSnapshot? snapshot, int? stateMessageId)> GetSnapshotAsync(long userId, CancellationToken ct);
    Task SetStateMessageIdAsync(long userId, int messageId, CancellationToken ct);
}

public sealed class BlackjackService(
    IBlackjackHandStore hands,
    IEconomicsService economics,
    IAnalyticsService analytics,
    IDomainEventBus events,
    IOptions<BlackjackOptions> options) : IBlackjackService
{
    private readonly BlackjackOptions _opts = options.Value;

    public async Task<BlackjackResult> StartAsync(long userId, string displayName, long chatId, int bet, CancellationToken ct)
    {
        if (bet < _opts.MinBet || bet > _opts.MaxBet)
            return new BlackjackResult(BlackjackError.InvalidBet, null);

        var existing = await hands.FindAsync(userId, ct);
        if (existing != null)
            return new BlackjackResult(BlackjackError.HandInProgress, null);

        await economics.EnsureUserAsync(userId, displayName, ct);
        if (!await economics.TryDebitAsync(userId, bet, "blackjack.start", ct))
        {
            analytics.Track("blackjack", "not_enough_coins", new Dictionary<string, object?>
            {
                ["user_id"] = userId, ["chat_id"] = chatId, ["bet"] = bet,
            });
            return new BlackjackResult(BlackjackError.NotEnoughCoins, null);
        }

        var deck = Deck.BuildShuffled();
        var player = Deck.Draw(ref deck, 2);
        var dealer = Deck.Draw(ref deck, 2);

        var hand = new BlackjackHandRow(
            UserId: userId,
            ChatId: chatId,
            Bet: bet,
            PlayerCards: string.Join(" ", player),
            DealerCards: string.Join(" ", dealer),
            DeckState: deck,
            StateMessageId: null,
            CreatedAt: DateTimeOffset.UtcNow);

        analytics.Track("blackjack", "start", new Dictionary<string, object?>
        {
            ["user_id"] = userId, ["chat_id"] = chatId, ["bet"] = bet,
        });

        if (BlackjackHandValue.IsNaturalBlackjack(player))
            return await SettleAsync(hand, doubled: false, persisted: false, ct);

        await hands.InsertAsync(hand, ct);
        var balance = await economics.GetBalanceAsync(userId, ct);
        return new BlackjackResult(BlackjackError.None, BuildSnapshot(hand, balance, revealed: false), hand.StateMessageId);
    }

    public async Task<BlackjackResult> HitAsync(long userId, CancellationToken ct)
    {
        var hand = await hands.FindAsync(userId, ct);
        if (hand == null) return new BlackjackResult(BlackjackError.NoActiveHand, null);

        var deck = hand.DeckState;
        var drawn = Deck.Draw(ref deck, 1);
        var player = Deck.Parse(hand.PlayerCards).Append(drawn[0]).ToArray();
        var updated = hand with { PlayerCards = string.Join(" ", player), DeckState = deck };

        if (BlackjackHandValue.Compute(player) > 21)
            return await SettleAsync(updated, doubled: false, persisted: true, ct);

        await hands.UpdateAsync(updated, ct);
        var balance = await economics.GetBalanceAsync(userId, ct);
        return new BlackjackResult(BlackjackError.None, BuildSnapshot(updated, balance, revealed: false), updated.StateMessageId);
    }

    public async Task<BlackjackResult> StandAsync(long userId, CancellationToken ct)
    {
        var hand = await hands.FindAsync(userId, ct);
        if (hand == null) return new BlackjackResult(BlackjackError.NoActiveHand, null);
        return await SettleAsync(hand, doubled: false, persisted: true, ct);
    }

    public async Task<BlackjackResult> DoubleAsync(long userId, CancellationToken ct)
    {
        var hand = await hands.FindAsync(userId, ct);
        if (hand == null) return new BlackjackResult(BlackjackError.NoActiveHand, null);

        var player = Deck.Parse(hand.PlayerCards);
        if (player.Length != 2) return new BlackjackResult(BlackjackError.CannotDouble, null);

        if (!await economics.TryDebitAsync(userId, hand.Bet, "blackjack.double", ct))
            return new BlackjackResult(BlackjackError.NotEnoughCoins, null);

        var deck = hand.DeckState;
        var drawn = Deck.Draw(ref deck, 1);
        var updated = hand with
        {
            Bet = hand.Bet * 2,
            PlayerCards = string.Join(" ", player.Append(drawn[0])),
            DeckState = deck,
        };

        return await SettleAsync(updated, doubled: true, persisted: true, ct);
    }

    public async Task<(BlackjackSnapshot? snapshot, int? stateMessageId)> GetSnapshotAsync(long userId, CancellationToken ct)
    {
        var hand = await hands.FindAsync(userId, ct);
        if (hand == null) return (null, null);
        var balance = await economics.GetBalanceAsync(userId, ct);
        return (BuildSnapshot(hand, balance, revealed: false), hand.StateMessageId);
    }

    public Task SetStateMessageIdAsync(long userId, int messageId, CancellationToken ct) =>
        hands.SetStateMessageIdAsync(userId, messageId, ct);

    private async Task<BlackjackResult> SettleAsync(BlackjackHandRow hand, bool doubled, bool persisted, CancellationToken ct)
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
        if (payout > 0) await economics.CreditAsync(hand.UserId, payout, "blackjack.settle", ct);

        if (persisted) await hands.DeleteAsync(hand.UserId, ct);

        var balance = await economics.GetBalanceAsync(hand.UserId, ct);

        analytics.Track("blackjack", "end", new Dictionary<string, object?>
        {
            ["user_id"] = hand.UserId, ["chat_id"] = hand.ChatId,
            ["bet"] = hand.Bet, ["payout"] = payout,
            ["player_total"] = playerTotal, ["dealer_total"] = dealerTotal,
            ["outcome"] = outcome.ToString(), ["doubled"] = doubled,
        });

        await events.PublishAsync(
            new BlackjackHandCompleted(hand.UserId, hand.ChatId, hand.Bet, payout,
                playerTotal, dealerTotal, outcome.ToString(), doubled,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()),
            ct);

        var snapshot = new BlackjackSnapshot(
            player, [.. dealer], playerTotal, dealerTotal,
            hand.Bet, balance,
            DealerHoleRevealed: true, CanDouble: false,
            Outcome: outcome, Payout: payout);
        return new BlackjackResult(BlackjackError.None, snapshot, hand.StateMessageId);
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

    private static BlackjackSnapshot BuildSnapshot(BlackjackHandRow hand, int balance, bool revealed)
    {
        var player = Deck.Parse(hand.PlayerCards);
        var dealer = Deck.Parse(hand.DealerCards);
        var playerTotal = BlackjackHandValue.Compute(player);
        var dealerVisible = revealed ? dealer : dealer.Take(1).ToArray();
        var dealerTotal = BlackjackHandValue.Compute(dealerVisible);
        var canDouble = player.Length == 2 && balance >= hand.Bet;
        return new BlackjackSnapshot(
            player, dealerVisible, playerTotal, dealerTotal,
            hand.Bet, balance, revealed, canDouble,
            Outcome: null, Payout: 0);
    }
}

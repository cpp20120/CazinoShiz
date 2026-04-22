// ─────────────────────────────────────────────────────────────────────────────
// BowlingService — place a 🎳 bet, resolve on roll.
// Telegram bowling dice values 1–6:
//   1 = gutter, 2–3 = few pins, 4 = several pins, 5 = most pins, 6 = strike.
// Payout: 4→x2, 5→x3, 6 (strike)→x6. Everything else burns the bet.
// ─────────────────────────────────────────────────────────────────────────────

using BotFramework.Host;
using BotFramework.Sdk;

namespace Games.Bowling;

public interface IBowlingService
{
    Task<BowlingBetResult> PlaceBetAsync(long userId, string displayName, long chatId, int amount, CancellationToken ct);
    Task<BowlingRollResult> RollAsync(long userId, string displayName, long chatId, int face, CancellationToken ct);
}

public sealed class BowlingService(
    IEconomicsService economics,
    IAnalyticsService analytics,
    IBowlingBetStore bets,
    IDomainEventBus events) : IBowlingService
{
    public static readonly IReadOnlyDictionary<int, int> Multipliers = new Dictionary<int, int>
    {
        [1] = 0, [2] = 0, [3] = 0, [4] = 2, [5] = 3, [6] = 6,
    };

    public async Task<BowlingBetResult> PlaceBetAsync(long userId, string displayName, long chatId, int amount, CancellationToken ct)
    {
        if (amount <= 0) return BowlingBetResult.Fail(BowlingBetError.InvalidAmount);

        await economics.EnsureUserAsync(userId, chatId, displayName, ct);
        var balance = await economics.GetBalanceAsync(userId, chatId, ct);
        if (amount > balance) return BowlingBetResult.Fail(BowlingBetError.NotEnoughCoins, balance);

        var existing = await bets.FindAsync(userId, chatId, ct);
        if (existing != null) return BowlingBetResult.Fail(BowlingBetError.AlreadyPending, balance, existing.Amount);

        if (!await economics.TryDebitAsync(userId, chatId, amount, "bowling.bet", ct))
            return BowlingBetResult.Fail(BowlingBetError.NotEnoughCoins, balance);

        await bets.InsertAsync(new BowlingBet(userId, chatId, amount, DateTimeOffset.UtcNow), ct);

        analytics.Track("bowling", "bet", new Dictionary<string, object?>
        {
            ["user_id"] = userId, ["chat_id"] = chatId, ["amount"] = amount,
        });

        return new BowlingBetResult(BowlingBetError.None, amount, balance - amount);
    }

    public async Task<BowlingRollResult> RollAsync(long userId, string displayName, long chatId, int face, CancellationToken ct)
    {
        var bet = await bets.FindAsync(userId, chatId, ct);
        if (bet == null) return new BowlingRollResult(BowlingRollOutcome.NoBet);

        await economics.EnsureUserAsync(userId, chatId, displayName, ct);
        var multiplier = Multipliers.TryGetValue(face, out var m) ? m : 0;
        var payout = bet.Amount * multiplier;

        if (payout > 0)
            await economics.CreditAsync(userId, chatId, payout, "bowling.payout", ct);

        await bets.DeleteAsync(userId, chatId, ct);
        var balance = await economics.GetBalanceAsync(userId, chatId, ct);

        analytics.Track("bowling", "roll", new Dictionary<string, object?>
        {
            ["user_id"] = userId, ["chat_id"] = chatId, ["face"] = face,
            ["bet"] = bet.Amount, ["multiplier"] = multiplier, ["payout"] = payout,
        });

        await events.PublishAsync(
            new BowlingRollCompleted(userId, chatId, face, bet.Amount, multiplier, payout,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()),
            ct);

        return new BowlingRollResult(BowlingRollOutcome.Rolled, face, bet.Amount, multiplier, payout, balance);
    }
}

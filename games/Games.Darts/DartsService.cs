// ─────────────────────────────────────────────────────────────────────────────
// DartsService — place a dart bet, resolve on 🎯 throw.
// Payout table: 4→x2, 5→x3, 6 (bullseye)→x6. Mirrors DiceCube's shape but with
// a sharper reward curve on the bullseye.
// ─────────────────────────────────────────────────────────────────────────────

using BotFramework.Host;
using BotFramework.Sdk;

namespace Games.Darts;

public interface IDartsService
{
    Task<DartsBetResult> PlaceBetAsync(long userId, string displayName, long chatId, int amount, CancellationToken ct);
    Task<DartsThrowResult> ThrowAsync(long userId, string displayName, long chatId, int face, CancellationToken ct);
}

public sealed class DartsService(
    IEconomicsService economics,
    IAnalyticsService analytics,
    IDartsBetStore bets,
    IDomainEventBus events) : IDartsService
{
    public static readonly IReadOnlyDictionary<int, int> Multipliers = new Dictionary<int, int>
    {
        [1] = 0, [2] = 0, [3] = 0, [4] = 2, [5] = 3, [6] = 6,
    };

    public async Task<DartsBetResult> PlaceBetAsync(long userId, string displayName, long chatId, int amount, CancellationToken ct)
    {
        if (amount <= 0) return DartsBetResult.Fail(DartsBetError.InvalidAmount);

        await economics.EnsureUserAsync(userId, displayName, ct);
        var balance = await economics.GetBalanceAsync(userId, ct);
        if (amount > balance) return DartsBetResult.Fail(DartsBetError.NotEnoughCoins, balance);

        var existing = await bets.FindAsync(userId, chatId, ct);
        if (existing != null) return DartsBetResult.Fail(DartsBetError.AlreadyPending, balance, existing.Amount);

        if (!await economics.TryDebitAsync(userId, amount, "darts.bet", ct))
            return DartsBetResult.Fail(DartsBetError.NotEnoughCoins, balance);

        await bets.InsertAsync(new DartsBet(userId, chatId, amount, DateTimeOffset.UtcNow), ct);

        analytics.Track("darts", "bet", new Dictionary<string, object?>
        {
            ["user_id"] = userId, ["chat_id"] = chatId, ["amount"] = amount,
        });

        return new DartsBetResult(DartsBetError.None, amount, balance - amount);
    }

    public async Task<DartsThrowResult> ThrowAsync(long userId, string displayName, long chatId, int face, CancellationToken ct)
    {
        var bet = await bets.FindAsync(userId, chatId, ct);
        if (bet == null) return new DartsThrowResult(DartsThrowOutcome.NoBet);

        await economics.EnsureUserAsync(userId, displayName, ct);
        var multiplier = Multipliers.TryGetValue(face, out var m) ? m : 0;
        var payout = bet.Amount * multiplier;

        if (payout > 0)
            await economics.CreditAsync(userId, payout, "darts.payout", ct);

        await bets.DeleteAsync(userId, chatId, ct);
        var balance = await economics.GetBalanceAsync(userId, ct);

        analytics.Track("darts", "throw", new Dictionary<string, object?>
        {
            ["user_id"] = userId, ["chat_id"] = chatId, ["face"] = face,
            ["bet"] = bet.Amount, ["multiplier"] = multiplier, ["payout"] = payout,
        });

        await events.PublishAsync(
            new DartsThrowCompleted(userId, chatId, face, bet.Amount, multiplier, payout,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()),
            ct);

        return new DartsThrowResult(DartsThrowOutcome.Thrown, face, bet.Amount, multiplier, payout, balance);
    }
}

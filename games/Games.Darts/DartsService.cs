// ─────────────────────────────────────────────────────────────────────────────
// DartsService — place a dart bet, resolve on 🎯 throw.
// Payout: 4→x1, 5→x2, 6 (bullseye)→x2. Uniform d6 ⇒ EV 5/6 of stake (house +EV).
// ─────────────────────────────────────────────────────────────────────────────

using BotFramework.Host;
using BotFramework.Sdk;
using Microsoft.Extensions.Options;

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
    IDomainEventBus events,
    IOptions<DartsOptions> options) : IDartsService
{
    private readonly int _maxBet = options.Value.MaxBet;
    public static readonly IReadOnlyDictionary<int, int> Multipliers = new Dictionary<int, int>
    {
        [1] = 0, [2] = 0, [3] = 0, [4] = 1, [5] = 2, [6] = 2,
    };

    public async Task<DartsBetResult> PlaceBetAsync(long userId, string displayName, long chatId, int amount, CancellationToken ct)
    {
        if (amount <= 0 || amount > _maxBet) return DartsBetResult.Fail(DartsBetError.InvalidAmount);

        await economics.EnsureUserAsync(userId, chatId, displayName, ct);
        var balance = await economics.GetBalanceAsync(userId, chatId, ct);
        if (amount > balance) return DartsBetResult.Fail(DartsBetError.NotEnoughCoins, balance);

        var existing = await bets.FindAsync(userId, chatId, ct);
        if (existing != null) return DartsBetResult.Fail(DartsBetError.AlreadyPending, balance, existing.Amount);

        if (!await economics.TryDebitAsync(userId, chatId, amount, "darts.bet", ct))
            return DartsBetResult.Fail(DartsBetError.NotEnoughCoins, balance);

        if (!await bets.InsertAsync(new DartsBet(userId, chatId, amount, DateTimeOffset.UtcNow), ct))
        {
            await economics.CreditAsync(userId, chatId, amount, "darts.bet.refund", ct);
            return DartsBetResult.Fail(DartsBetError.AlreadyPending, balance);
        }

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

        await economics.EnsureUserAsync(userId, chatId, displayName, ct);
        var multiplier = Multipliers.TryGetValue(face, out var m) ? m : 0;
        var payout = bet.Amount * multiplier;

        if (payout > 0)
            await economics.CreditAsync(userId, chatId, payout, "darts.payout", ct);

        await bets.DeleteAsync(userId, chatId, ct);
        var balance = await economics.GetBalanceAsync(userId, chatId, ct);

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

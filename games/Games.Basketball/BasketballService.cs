// ─────────────────────────────────────────────────────────────────────────────
// BasketballService — place a 🏀 bet, resolve on throw.
// Telegram basketball dice values 1–5:
//   1-2 = rebound off rim/miss, 3 = bounces off ring, 4 = scored, 5 = clean swish.
// Payout: 4→x2 (scored), 5→x3 (swish). Everything else burns the bet.
// ─────────────────────────────────────────────────────────────────────────────

using BotFramework.Host;
using BotFramework.Sdk;
using Microsoft.Extensions.Options;

namespace Games.Basketball;

public interface IBasketballService
{
    Task<BasketballBetResult> PlaceBetAsync(long userId, string displayName, long chatId, int amount, CancellationToken ct);
    Task<BasketballThrowResult> ThrowAsync(long userId, string displayName, long chatId, int face, CancellationToken ct);
}

public sealed class BasketballService(
    IEconomicsService economics,
    IAnalyticsService analytics,
    IBasketballBetStore bets,
    IDomainEventBus events,
    IOptions<BasketballOptions> options) : IBasketballService
{
    private readonly int _maxBet = options.Value.MaxBet;
    public static readonly IReadOnlyDictionary<int, int> Multipliers = new Dictionary<int, int>
    {
        [1] = 0, [2] = 0, [3] = 0, [4] = 2, [5] = 3,
    };

    public async Task<BasketballBetResult> PlaceBetAsync(long userId, string displayName, long chatId, int amount, CancellationToken ct)
    {
        if (amount <= 0 || amount > _maxBet) return BasketballBetResult.Fail(BasketballBetError.InvalidAmount);

        await economics.EnsureUserAsync(userId, chatId, displayName, ct);
        var balance = await economics.GetBalanceAsync(userId, chatId, ct);
        if (amount > balance) return BasketballBetResult.Fail(BasketballBetError.NotEnoughCoins, balance);

        var existing = await bets.FindAsync(userId, chatId, ct);
        if (existing != null) return BasketballBetResult.Fail(BasketballBetError.AlreadyPending, balance, existing.Amount);

        if (!await economics.TryDebitAsync(userId, chatId, amount, "basketball.bet", ct))
            return BasketballBetResult.Fail(BasketballBetError.NotEnoughCoins, balance);

        if (!await bets.InsertAsync(new BasketballBet(userId, chatId, amount, DateTimeOffset.UtcNow), ct))
        {
            await economics.CreditAsync(userId, chatId, amount, "basketball.bet.refund", ct);
            return BasketballBetResult.Fail(BasketballBetError.NotEnoughCoins, balance);
        }

        analytics.Track("basketball", "bet", new Dictionary<string, object?>
        {
            ["user_id"] = userId, ["chat_id"] = chatId, ["amount"] = amount,
        });

        return new BasketballBetResult(BasketballBetError.None, amount, balance - amount);
    }

    public async Task<BasketballThrowResult> ThrowAsync(long userId, string displayName, long chatId, int face, CancellationToken ct)
    {
        var bet = await bets.FindAsync(userId, chatId, ct);
        if (bet == null) return new BasketballThrowResult(BasketballThrowOutcome.NoBet);

        await economics.EnsureUserAsync(userId, chatId, displayName, ct);
        var multiplier = Multipliers.TryGetValue(face, out var m) ? m : 0;
        var payout = bet.Amount * multiplier;

        if (payout > 0)
            await economics.CreditAsync(userId, chatId, payout, "basketball.payout", ct);

        await bets.DeleteAsync(userId, chatId, ct);
        var balance = await economics.GetBalanceAsync(userId, chatId, ct);

        analytics.Track("basketball", "throw", new Dictionary<string, object?>
        {
            ["user_id"] = userId, ["chat_id"] = chatId, ["face"] = face,
            ["bet"] = bet.Amount, ["multiplier"] = multiplier, ["payout"] = payout,
        });

        await events.PublishAsync(
            new BasketballThrowCompleted(userId, chatId, face, bet.Amount, multiplier, payout,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()),
            ct);

        return new BasketballThrowResult(BasketballThrowOutcome.Thrown, face, bet.Amount, multiplier, payout, balance);
    }
}

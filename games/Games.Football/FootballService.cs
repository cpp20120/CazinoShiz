// ─────────────────────────────────────────────────────────────────────────────
// FootballService — bet, then resolve on Telegram's ⚽ dice (values 1–5).
// Payout: 1–3 → x0, 4 → x2, 5 → x3. Uniform faces ⇒ EV 0.2·(2+3) = 1.0 (fair).
// ─────────────────────────────────────────────────────────────────────────────

using BotFramework.Host;
using BotFramework.Sdk;
using Microsoft.Extensions.Options;

namespace Games.Football;

public interface IFootballService
{
    Task<FootballBetResult> PlaceBetAsync(long userId, string displayName, long chatId, int amount, CancellationToken ct);
    Task<FootballThrowResult> ThrowAsync(long userId, string displayName, long chatId, int face, CancellationToken ct);
}

public sealed class FootballService(
    IEconomicsService economics,
    IAnalyticsService analytics,
    IFootballBetStore bets,
    IDomainEventBus events,
    IOptions<FootballOptions> options) : IFootballService
{
    private readonly int _maxBet = options.Value.MaxBet;
    public static readonly IReadOnlyDictionary<int, int> Multipliers = new Dictionary<int, int>
    {
        [1] = 0, [2] = 0, [3] = 0, [4] = 2, [5] = 3,
    };

    public async Task<FootballBetResult> PlaceBetAsync(long userId, string displayName, long chatId, int amount, CancellationToken ct)
    {
        if (amount <= 0 || amount > _maxBet) return FootballBetResult.Fail(FootballBetError.InvalidAmount);

        await economics.EnsureUserAsync(userId, chatId, displayName, ct);
        var balance = await economics.GetBalanceAsync(userId, chatId, ct);
        if (amount > balance) return FootballBetResult.Fail(FootballBetError.NotEnoughCoins, balance);

        var existing = await bets.FindAsync(userId, chatId, ct);
        if (existing != null) return FootballBetResult.Fail(FootballBetError.AlreadyPending, balance, existing.Amount);

        if (!await economics.TryDebitAsync(userId, chatId, amount, "football.bet", ct))
            return FootballBetResult.Fail(FootballBetError.NotEnoughCoins, balance);

        if (!await bets.InsertAsync(new FootballBet(userId, chatId, amount, DateTimeOffset.UtcNow), ct))
        {
            await economics.CreditAsync(userId, chatId, amount, "football.bet.refund", ct);
            return FootballBetResult.Fail(FootballBetError.AlreadyPending, balance);
        }

        analytics.Track("football", "bet", new Dictionary<string, object?>
        {
            ["user_id"] = userId, ["chat_id"] = chatId, ["amount"] = amount,
        });

        return new FootballBetResult(FootballBetError.None, amount, balance - amount);
    }

    public async Task<FootballThrowResult> ThrowAsync(long userId, string displayName, long chatId, int face, CancellationToken ct)
    {
        var bet = await bets.FindAsync(userId, chatId, ct);
        if (bet == null) return new FootballThrowResult(FootballThrowOutcome.NoBet);

        await economics.EnsureUserAsync(userId, chatId, displayName, ct);
        var multiplier = Multipliers.TryGetValue(face, out var m) ? m : 0;
        var payout = bet.Amount * multiplier;

        if (payout > 0)
            await economics.CreditAsync(userId, chatId, payout, "football.payout", ct);

        await bets.DeleteAsync(userId, chatId, ct);
        var balance = await economics.GetBalanceAsync(userId, chatId, ct);

        analytics.Track("football", "throw", new Dictionary<string, object?>
        {
            ["user_id"] = userId, ["chat_id"] = chatId, ["face"] = face,
            ["bet"] = bet.Amount, ["multiplier"] = multiplier, ["payout"] = payout,
        });

        await events.PublishAsync(
            new FootballThrowCompleted(userId, chatId, face, bet.Amount, multiplier, payout,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()),
            ct);

        return new FootballThrowResult(FootballThrowOutcome.Thrown, face, bet.Amount, multiplier, payout, balance);
    }
}

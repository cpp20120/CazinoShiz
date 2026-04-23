// ─────────────────────────────────────────────────────────────────────────────
// DiceCubeService — place a cube bet, resolve on 🎲 throw.
//
// Payout: credit = bet × mult(face). Defaults 1,2,2 ⇒ uniform d6 EV = 5/6 of stake
// (house +EV; old 1,2,3 was break-even for the house).
//
// MinSecondsBetweenBets: optional per-(user, chat) delay after a completed roll
// before the next /dice bet to reduce leaderboard / chat spam.
// ─────────────────────────────────────────────────────────────────────────────

using BotFramework.Host;
using BotFramework.Sdk;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Games.DiceCube;

public interface IDiceCubeService
{
    int Mult4 { get; }
    int Mult5 { get; }
    int Mult6 { get; }

    Task<CubeBetResult> PlaceBetAsync(long userId, string displayName, long chatId, int amount, CancellationToken ct);
    Task<CubeRollResult> RollAsync(long userId, string displayName, long chatId, int face, CancellationToken ct);
}

public sealed class DiceCubeService(
    IEconomicsService economics,
    IAnalyticsService analytics,
    IDiceCubeBetStore bets,
    IDomainEventBus events,
    IMemoryCache cache,
    IOptions<DiceCubeOptions> options) : IDiceCubeService
{
    private readonly DiceCubeOptions _opt = options.Value;
    private readonly int _maxBet = options.Value.MaxBet;
    private readonly IReadOnlyDictionary<int, int> _mults = BuildMultipliers(options.Value);

    public int Mult4 => _opt.Mult4;
    public int Mult5 => _opt.Mult5;
    public int Mult6 => _opt.Mult6;

    public static IReadOnlyDictionary<int, int> BuildMultipliers(DiceCubeOptions o) =>
        new Dictionary<int, int>
        {
            [1] = 0, [2] = 0, [3] = 0, [4] = o.Mult4, [5] = o.Mult5, [6] = o.Mult6,
        };

    private static string CooldownCacheKey(long userId, long chatId) => $"dicecube:lastroll:{userId}:{chatId}";

    public async Task<CubeBetResult> PlaceBetAsync(long userId, string displayName, long chatId, int amount, CancellationToken ct)
    {
        if (amount <= 0 || amount > _maxBet) return CubeBetResult.Fail(CubeBetError.InvalidAmount);

        await economics.EnsureUserAsync(userId, chatId, displayName, ct);
        var balance = await economics.GetBalanceAsync(userId, chatId, ct);
        if (amount > balance) return CubeBetResult.Fail(CubeBetError.NotEnoughCoins, balance);

        if (_opt.MinSecondsBetweenBets > 0
            && cache.TryGetValue(CooldownCacheKey(userId, chatId), out DateTimeOffset lastRoll))
        {
            var wait = (lastRoll + TimeSpan.FromSeconds(_opt.MinSecondsBetweenBets)) - DateTimeOffset.UtcNow;
            if (wait > TimeSpan.Zero)
            {
                var sec = (int)Math.Ceiling(wait.TotalSeconds);
                return CubeBetResult.CooldownWait(balance, sec);
            }
        }

        var existing = await bets.FindAsync(userId, chatId, ct);
        if (existing != null) return CubeBetResult.Fail(CubeBetError.AlreadyPending, balance, existing.Amount);

        if (!await economics.TryDebitAsync(userId, chatId, amount, "dicecube.bet", ct))
            return CubeBetResult.Fail(CubeBetError.NotEnoughCoins, balance);

        if (!await bets.InsertAsync(new DiceCubeBet(userId, chatId, amount, DateTimeOffset.UtcNow), ct))
        {
            await economics.CreditAsync(userId, chatId, amount, "dicecube.bet.refund", ct);
            return CubeBetResult.Fail(CubeBetError.AlreadyPending, balance);
        }

        analytics.Track("dicecube", "bet", new Dictionary<string, object?>
        {
            ["user_id"] = userId, ["chat_id"] = chatId, ["amount"] = amount,
        });

        return new CubeBetResult(CubeBetError.None, amount, balance - amount, 0, 0);
    }

    public async Task<CubeRollResult> RollAsync(long userId, string displayName, long chatId, int face, CancellationToken ct)
    {
        var bet = await bets.FindAsync(userId, chatId, ct);
        if (bet == null) return new CubeRollResult(CubeRollOutcome.NoBet);

        await economics.EnsureUserAsync(userId, chatId, displayName, ct);
        var multiplier = _mults.TryGetValue(face, out var m) ? m : 0;
        var payout = bet.Amount * multiplier;

        if (payout > 0)
            await economics.CreditAsync(userId, chatId, payout, "dicecube.payout", ct);

        await bets.DeleteAsync(userId, chatId, ct);
        if (_opt.MinSecondsBetweenBets > 0)
        {
            cache.Set(
                CooldownCacheKey(userId, chatId),
                DateTimeOffset.UtcNow,
                new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(6),
                });
        }

        var balance = await economics.GetBalanceAsync(userId, chatId, ct);

        analytics.Track("dicecube", "roll", new Dictionary<string, object?>
        {
            ["user_id"] = userId, ["chat_id"] = chatId, ["face"] = face,
            ["bet"] = bet.Amount, ["multiplier"] = multiplier, ["payout"] = payout,
        });

        await events.PublishAsync(
            new DiceCubeRollCompleted(userId, chatId, face, bet.Amount, multiplier, payout,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()),
            ct);

        return new CubeRollResult(CubeRollOutcome.Rolled, face, bet.Amount, multiplier, payout, balance);
    }
}

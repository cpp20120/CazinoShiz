// ─────────────────────────────────────────────────────────────────────────────
// DiceCubeService — place a cube bet, resolve on 🎲 throw.
//
// Payout: credit = bet × mult(face). Defaults 1,2,2 ⇒ uniform d6 EV = 5/6 of stake
// (house +EV; old 1,2,3 was break-even for the house).
//
// MinSecondsBetweenBets: optional per-(user, chat) delay after a completed roll
// before the next /dice bet to reduce leaderboard / chat spam.
//
// Mult4/5/6 are snapshotted on the bet row so pending rolls keep the odds from
// when the bet was placed after runtime config changes.
// ─────────────────────────────────────────────────────────────────────────────

using BotFramework.Host;
using BotFramework.Host.Services;
using BotFramework.Sdk;
using Microsoft.Extensions.Caching.Memory;

namespace Games.DiceCube;

public interface IDiceCubeService
{
    int Mult4 { get; }
    int Mult5 { get; }
    int Mult6 { get; }

    Task<CubeBetResult> PlaceBetAsync(long userId, string displayName, long chatId, int amount, CancellationToken ct);
    Task<CubeRollResult> RollAsync(long userId, string displayName, long chatId, int face, CancellationToken ct);

    /// <summary>Refund and clear pending bet when bot cannot deliver SendMessage/SendDice after debit.</summary>
    Task AbortPendingBetAfterSendDiceFailedAsync(long userId, long chatId, CancellationToken ct);
}

public sealed class DiceCubeService(
    IEconomicsService economics,
    IAnalyticsService analytics,
    IDiceCubeBetStore bets,
    IDomainEventBus events,
    IMemoryCache cache,
    IRuntimeTuningAccessor tuning,
    IMiniGameSessionGhostHeal ghostHeal,
    ITelegramDiceDailyRollLimiter telegramDiceRolls,
    IMiniGameSessionStore? sessions = null) : IDiceCubeService
{
    private IMiniGameSessionStore Sessions => sessions ?? NullMiniGameSessionStore.Instance;

    private DiceCubeOptions Cube => tuning.GetSection<DiceCubeOptions>(DiceCubeOptions.SectionName);

    public int Mult4 => Cube.Mult4;
    public int Mult5 => Cube.Mult5;
    public int Mult6 => Cube.Mult6;

    public static IReadOnlyDictionary<int, int> BuildMultipliers(DiceCubeOptions o) =>
        new Dictionary<int, int>
        {
            [1] = 0, [2] = 0, [3] = 0, [4] = o.Mult4, [5] = o.Mult5, [6] = o.Mult6,
        };

    private static string CooldownCacheKey(long userId, long chatId) => $"dicecube:lastroll:{userId}:{chatId}";

    public async Task<CubeBetResult> PlaceBetAsync(long userId, string displayName, long chatId, int amount, CancellationToken ct)
    {
        var cube = Cube;
        if (amount <= 0 || amount > cube.MaxBet) return CubeBetResult.Fail(CubeBetError.InvalidAmount);

        await economics.EnsureUserAsync(userId, chatId, displayName, ct);
        var balance = await economics.GetBalanceAsync(userId, chatId, ct);
        if (amount > balance) return CubeBetResult.Fail(CubeBetError.NotEnoughCoins, balance);

        if (cube.MinSecondsBetweenBets > 0
            && cache.TryGetValue(CooldownCacheKey(userId, chatId), out DateTimeOffset lastRoll))
        {
            var wait = (lastRoll + TimeSpan.FromSeconds(cube.MinSecondsBetweenBets)) - DateTimeOffset.UtcNow;
            if (wait > TimeSpan.Zero)
            {
                var sec = (int)Math.Ceiling(wait.TotalSeconds);
                return CubeBetResult.CooldownWait(balance, sec);
            }
        }

        var session = await BotMiniGamePlaceBetSession.TryBeginWithGhostHealAsync(
            userId,
            chatId,
            MiniGameIds.DiceCube,
            async c =>
            {
                if (await bets.FindAsync(userId, chatId, c) == null)
                {
                    BotMiniGameSession.ClearCompletedRound(userId, chatId, MiniGameIds.DiceCube);
                    await Sessions.ClearCompletedRoundAsync(userId, chatId, MiniGameIds.DiceCube, c);
                }
            },
            ghostHeal,
            Sessions,
            ct);
        if (!session.Ok)
            return new CubeBetResult(CubeBetError.BusyOtherGame, 0, balance, 0, 0, session.Blocker, 0, 0);

        var existing = await bets.FindAsync(userId, chatId, ct);
        if (existing != null) return CubeBetResult.Fail(CubeBetError.AlreadyPending, balance, existing.Amount);

        var gate = await telegramDiceRolls.TryConsumeRollAsync(userId, chatId, ct);
        if (gate.Status == TelegramDiceRollGateStatus.LimitExceeded)
            return new CubeBetResult(
                CubeBetError.DailyRollLimit, 0, balance, 0, 0, null, gate.UsedToday, gate.Limit);

        if (!await economics.TryDebitAsync(userId, chatId, amount, "dicecube.bet", ct))
        {
            await telegramDiceRolls.TryRefundRollAsync(userId, chatId, ct);
            return CubeBetResult.Fail(CubeBetError.NotEnoughCoins, balance);
        }

        cube = Cube;
        if (!await bets.InsertAsync(
                new DiceCubeBet(
                    userId,
                    chatId,
                    amount,
                    DateTimeOffset.UtcNow,
                    cube.Mult4,
                    cube.Mult5,
                    cube.Mult6),
                ct))
        {
            await telegramDiceRolls.TryRefundRollAsync(userId, chatId, ct);
            await economics.CreditAsync(userId, chatId, amount, "dicecube.bet.refund", ct);
            return CubeBetResult.Fail(CubeBetError.AlreadyPending, balance);
        }

        BotMiniGameSession.RegisterPlacedBet(userId, chatId, MiniGameIds.DiceCube);
        await Sessions.RegisterPlacedBetAsync(userId, chatId, MiniGameIds.DiceCube, ct);

        analytics.Track("dicecube", "bet", new Dictionary<string, object?>
        {
            ["user_id"] = userId, ["chat_id"] = chatId, ["amount"] = amount,
        });

        return new CubeBetResult(CubeBetError.None, amount, balance - amount, 0, 0, null, 0, 0);
    }

    public async Task<CubeRollResult> RollAsync(long userId, string displayName, long chatId, int face, CancellationToken ct)
    {
        var bet = await bets.FindAsync(userId, chatId, ct);
        if (bet == null) return new CubeRollResult(CubeRollOutcome.NoBet);

        await economics.EnsureUserAsync(userId, chatId, displayName, ct);
        var rule = new DiceCubeOptions
        {
            Mult4 = bet.Mult4,
            Mult5 = bet.Mult5,
            Mult6 = bet.Mult6,
        };
        var mults = BuildMultipliers(rule);
        var multiplier = mults.TryGetValue(face, out var m) ? m : 0;
        var payout = bet.Amount * multiplier;

        if (payout > 0)
            await economics.CreditAsync(userId, chatId, payout, "dicecube.payout", ct);

        await bets.DeleteAsync(userId, chatId, ct);
        BotMiniGameSession.ClearCompletedRound(userId, chatId, MiniGameIds.DiceCube);
        await Sessions.ClearCompletedRoundAsync(userId, chatId, MiniGameIds.DiceCube, ct);
        var cube = Cube;
        if (cube.MinSecondsBetweenBets > 0)
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

    public async Task AbortPendingBetAfterSendDiceFailedAsync(long userId, long chatId, CancellationToken ct)
    {
        var bet = await bets.FindAsync(userId, chatId, ct);
        if (bet == null) return;

        await economics.CreditAsync(userId, chatId, bet.Amount, "dicecube.bot_dice.failed", ct);
        await bets.DeleteAsync(userId, chatId, ct);
        BotMiniGameSession.ClearCompletedRound(userId, chatId, MiniGameIds.DiceCube);
        await Sessions.ClearCompletedRoundAsync(userId, chatId, MiniGameIds.DiceCube, ct);
        await telegramDiceRolls.TryRefundRollAsync(userId, chatId, ct);
    }
}

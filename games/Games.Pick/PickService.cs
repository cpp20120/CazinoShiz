// ─────────────────────────────────────────────────────────────────────────────
// PickService — single-player /pick game.
//
// One round flow (top-level bet):
//   1. validate amount + variants + backed indices (1..N, deduped),
//   2. ensure wallet, check balance, debit stake (reason "pick.bet"),
//   3. roll uniform index in [0, N),
//   4. if rolled index ∈ backed: gross = bet × (N / k); credit floor(gross × (1 − HouseEdge)) + streak bonus,
//      streak++. Else: streak resets to 0.
//   5. if won and ChainMaxDepth > 0 and depth < cap: register a PickChainState
//      so the handler can show a "double or nothing" inline button.
//
// Chain hops (entered via ContinueChainAsync): same flow but skips streak math
// (chains are flavour, not progression) and uses the previous payout as the
// new stake.
//
// Balance scope = chat id (matches every other casino game in the host).
// ─────────────────────────────────────────────────────────────────────────────

using BotFramework.Host;
using BotFramework.Host.Services;
using Microsoft.Extensions.Options;

namespace Games.Pick;

public interface IPickService
{
    Task<PickResult> PickAsync(
        long userId,
        string displayName,
        long chatId,
        int amount,
        IReadOnlyList<string> variants,
        IReadOnlyList<int> backedIndices,
        CancellationToken ct);

    /// <summary>Continue an existing chain. Caller has already claimed the chain state.</summary>
    Task<PickResult> ContinueChainAsync(PickChainState chain, CancellationToken ct);
}

public sealed partial class PickService(
    IEconomicsService economics,
    IAnalyticsService analytics,
    PickStreakStore streaks,
    PickChainStore chains,
    IOptions<PickOptions> options,
    ILogger<PickService> logger) : IPickService
{
    private readonly PickOptions _opts = options.Value;

    public async Task<PickResult> PickAsync(
        long userId,
        string displayName,
        long chatId,
        int amount,
        IReadOnlyList<string> variants,
        IReadOnlyList<int> backedIndices,
        CancellationToken ct)
    {
        var validation = ValidateBet(amount, variants, backedIndices);
        if (validation is { } early) return early;

        await economics.EnsureUserAsync(userId, chatId, displayName, ct);
        var balance = await economics.GetBalanceAsync(userId, chatId, ct);
        if (amount > balance)
            return BalanceFail(amount, balance, variants, backedIndices);

        if (!await economics.TryDebitAsync(userId, chatId, amount, "pick.bet", ct))
        {
            balance = await economics.GetBalanceAsync(userId, chatId, ct);
            return BalanceFail(amount, balance, variants, backedIndices);
        }

        return await SettleAsync(
            userId, displayName, chatId, amount,
            variants, backedIndices,
            depth: 0,
            applyStreak: true,
            ct);
    }

    public async Task<PickResult> ContinueChainAsync(PickChainState chain, CancellationToken ct)
    {
        await economics.EnsureUserAsync(chain.UserId, chain.ChatId, chain.DisplayName, ct);
        var balance = await economics.GetBalanceAsync(chain.UserId, chain.ChatId, ct);
        if (chain.StakeForNext > balance)
            return BalanceFail(chain.StakeForNext, balance, chain.Variants, chain.BackedIndices);

        if (!await economics.TryDebitAsync(chain.UserId, chain.ChatId, chain.StakeForNext, "pick.chain.bet", ct))
        {
            balance = await economics.GetBalanceAsync(chain.UserId, chain.ChatId, ct);
            return BalanceFail(chain.StakeForNext, balance, chain.Variants, chain.BackedIndices);
        }

        return await SettleAsync(
            chain.UserId, chain.DisplayName, chain.ChatId, chain.StakeForNext,
            chain.Variants, chain.BackedIndices,
            depth: chain.Depth,
            applyStreak: false,
            ct);
    }

    // ── core settle ───────────────────────────────────────────────────────────

    private async Task<PickResult> SettleAsync(
        long userId,
        string displayName,
        long chatId,
        int stake,
        IReadOnlyList<string> variants,
        IReadOnlyList<int> backedIndices,
        int depth,
        bool applyStreak,
        CancellationToken ct)
    {
        var pickedIndex = Random.Shared.Next(variants.Count);
        var won = backedIndices.Contains(pickedIndex);
        var streakBefore = streaks.Get(userId, chatId);
        var streakAfter = streakBefore;

        var reason = depth == 0 ? "pick" : "pick.chain";
        var grossPayout = 0;
        var streakBonus = 0;
        var totalCredit = 0;

        if (won)
        {
            grossPayout = ComputePayout(stake, variants.Count, backedIndices.Count);
            if (applyStreak)
            {
                streakAfter = streaks.Increment(userId, chatId);
                streakBonus = ComputeStreakBonus(stake, streakAfter);
            }
            totalCredit = grossPayout + streakBonus;
            if (totalCredit > 0)
                await economics.CreditAsync(userId, chatId, totalCredit, $"{reason}.win", ct);
        }
        else if (applyStreak)
        {
            streaks.Reset(userId, chatId);
            streakAfter = 0;
        }

        var newBalance = await economics.GetBalanceAsync(userId, chatId, ct);
        var net = totalCredit - stake;

        Guid? chainGuid = null;
        if (won && _opts.ChainMaxDepth > 0 && depth < _opts.ChainMaxDepth)
        {
            // Stake for the next hop = entire payout (gross+bonus). User can
            // bow out by ignoring the button; balance already reflects the win.
            var stakeForNext = totalCredit;
            if (stakeForNext > 0 && (_opts.MaxBet <= 0 || stakeForNext <= _opts.MaxBet))
            {
                var id = Guid.NewGuid();
                chains.Add(new PickChainState(
                    Id: id,
                    UserId: userId,
                    ChatId: chatId,
                    DisplayName: displayName,
                    StakeForNext: stakeForNext,
                    Depth: depth + 1,
                    Variants: variants,
                    BackedIndices: backedIndices,
                    ExpiresAt: DateTimeOffset.UtcNow.AddSeconds(Math.Max(5, _opts.ChainTtlSeconds))));
                chainGuid = id;
            }
        }

        analytics.Track("pick", "roll", new Dictionary<string, object?>
        {
            ["user_id"] = userId,
            ["chat_id"] = chatId,
            ["bet"] = stake,
            ["variants"] = variants.Count,
            ["backed"] = backedIndices.Count,
            ["picked_index"] = pickedIndex,
            ["won"] = won,
            ["payout"] = totalCredit,
            ["streak_after"] = streakAfter,
            ["chain_depth"] = depth,
            ["chained"] = depth > 0,
        });

        LogRoll(userId, chatId, stake, variants.Count, backedIndices.Count, pickedIndex, won, totalCredit, depth);

        return new PickResult(
            Error: PickError.None,
            Bet: stake,
            Balance: newBalance,
            Payout: totalCredit,
            Net: net,
            StreakBonus: streakBonus,
            StreakBefore: streakBefore,
            StreakAfter: streakAfter,
            PickedIndex: pickedIndex,
            Won: won,
            ChainDepth: depth,
            ChainGuid: chainGuid,
            Variants: variants,
            BackedIndices: backedIndices);
    }

    private int ComputePayout(int stake, int variantsCount, int backedCount)
    {
        // Fair gross = stake × (N / k). House keeps HouseEdge of that.
        var gross = (double)stake * variantsCount / Math.Max(1, backedCount);
        var net = gross * (1.0 - Math.Clamp(_opts.HouseEdge, 0.0, 1.0));
        return (int)Math.Max(0, Math.Floor(net));
    }

    private int ComputeStreakBonus(int stake, int streakAfter)
    {
        // 1st win adds nothing (streakAfter==1 → factor 0). Each subsequent win
        // adds StreakBonusPerWin * stake, capped at StreakCap factor.
        var factor = Math.Min(Math.Max(0, streakAfter - 1), Math.Max(0, _opts.StreakCap));
        var bonus = stake * factor * _opts.StreakBonusPerWin;
        return (int)Math.Max(0, Math.Floor(bonus));
    }

    // ── validation helpers ────────────────────────────────────────────────────

    private PickResult? ValidateBet(int amount, IReadOnlyList<string> variants, IReadOnlyList<int> backedIndices)
    {
        if (variants.Count < _opts.MinVariants)
            return Fail(PickError.NotEnoughVariants, amount, variants, backedIndices);

        if (variants.Count > _opts.MaxVariants)
            return Fail(PickError.TooManyVariants, amount, variants, backedIndices);

        if (amount <= 0 || (_opts.MaxBet > 0 && amount > _opts.MaxBet))
            return Fail(PickError.InvalidAmount, amount, variants, backedIndices);

        if (backedIndices.Count == 0 || backedIndices.Count >= variants.Count)
            return Fail(PickError.InvalidChoice, amount, variants, backedIndices);

        foreach (var i in backedIndices)
        {
            if (i < 0 || i >= variants.Count)
                return Fail(PickError.InvalidChoice, amount, variants, backedIndices);
        }

        return null;
    }

    private static PickResult Fail(
        PickError err, int amount, IReadOnlyList<string> variants, IReadOnlyList<int> backed) =>
        new(err, amount, 0, 0, 0, 0, 0, 0, -1, false, 0, null, variants, backed);

    private static PickResult BalanceFail(
        int amount, int balance, IReadOnlyList<string> variants, IReadOnlyList<int> backed) =>
        new(PickError.NotEnoughCoins, amount, balance, 0, 0, 0, 0, 0, -1, false, 0, null, variants, backed);

    [LoggerMessage(EventId = 5910, Level = LogLevel.Information,
        Message = "pick.roll user={UserId} chat={ChatId} bet={Bet} N={Variants} k={Backed} idx={Picked} won={Won} payout={Payout} depth={Depth}")]
    partial void LogRoll(long userId, long chatId, int bet, int variants, int backed, int picked, bool won, int payout, int depth);
}

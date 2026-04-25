// ─────────────────────────────────────────────────────────────────────────────
// BowlingService — place a 🎳 bet, resolve on roll.
// Telegram bowling dice values 1–6:
//   1 = gutter, 2–3 = few pins, 4 = several pins, 5 = most pins, 6 = strike.
// Payout: 4→x1, 5→x2, 6 (strike)→x2 (uniform d6 EV 5/6 of stake; house +EV).
// ─────────────────────────────────────────────────────────────────────────────

using BotFramework.Host;
using BotFramework.Host.Services;
using BotFramework.Sdk;

namespace Games.Bowling;

public interface IBowlingService
{
    Task<BowlingBetResult> PlaceBetAsync(long userId, string displayName, long chatId, int amount, CancellationToken ct);
    Task<BowlingRollResult> RollAsync(long userId, string displayName, long chatId, int face, CancellationToken ct);
    Task AbortPendingBetAfterSendDiceFailedAsync(long userId, long chatId, CancellationToken ct);
}

public sealed class BowlingService(
    IEconomicsService economics,
    IAnalyticsService analytics,
    IBowlingBetStore bets,
    IDomainEventBus events,
    IRuntimeTuningAccessor tuning,
    IMiniGameSessionGhostHeal ghostHeal,
    ITelegramDiceDailyRollLimiter telegramDiceRolls) : IBowlingService
{
    public static readonly IReadOnlyDictionary<int, int> Multipliers = new Dictionary<int, int>
    {
        [1] = 0, [2] = 0, [3] = 0, [4] = 1, [5] = 2, [6] = 2,
    };

    public async Task<BowlingBetResult> PlaceBetAsync(long userId, string displayName, long chatId, int amount, CancellationToken ct)
    {
        var maxBet = tuning.GetSection<BowlingOptions>(BowlingOptions.SectionName).MaxBet;
        if (amount <= 0 || amount > maxBet) return BowlingBetResult.Fail(BowlingBetError.InvalidAmount);

        await economics.EnsureUserAsync(userId, chatId, displayName, ct);
        var balance = await economics.GetBalanceAsync(userId, chatId, ct);
        if (amount > balance) return BowlingBetResult.Fail(BowlingBetError.NotEnoughCoins, balance);

        var session = await BotMiniGamePlaceBetSession.TryBeginWithGhostHealAsync(
            userId,
            chatId,
            MiniGameIds.Bowling,
            async c =>
            {
                if (await bets.FindAsync(userId, chatId, c) == null)
                    BotMiniGameSession.ClearCompletedRound(userId, chatId, MiniGameIds.Bowling);
            },
            ghostHeal,
            ct);
        if (!session.Ok)
            return new BowlingBetResult(BowlingBetError.BusyOtherGame, 0, balance, 0, session.Blocker, 0, 0);

        var existing = await bets.FindAsync(userId, chatId, ct);
        if (existing != null) return BowlingBetResult.Fail(BowlingBetError.AlreadyPending, balance, existing.Amount);

        var gate = await telegramDiceRolls.TryConsumeRollAsync(userId, chatId, ct);
        if (gate.Status == TelegramDiceRollGateStatus.LimitExceeded)
            return new BowlingBetResult(
                BowlingBetError.DailyRollLimit, 0, balance, 0, null, gate.UsedToday, gate.Limit);

        if (!await economics.TryDebitAsync(userId, chatId, amount, "bowling.bet", ct))
        {
            await telegramDiceRolls.TryRefundRollAsync(userId, chatId, ct);
            return BowlingBetResult.Fail(BowlingBetError.NotEnoughCoins, balance);
        }

        if (!await bets.InsertAsync(new BowlingBet(userId, chatId, amount, DateTimeOffset.UtcNow), ct))
        {
            await telegramDiceRolls.TryRefundRollAsync(userId, chatId, ct);
            await economics.CreditAsync(userId, chatId, amount, "bowling.bet.refund", ct);
            return BowlingBetResult.Fail(BowlingBetError.AlreadyPending, balance);
        }

        BotMiniGameSession.RegisterPlacedBet(userId, chatId, MiniGameIds.Bowling);

        analytics.Track("bowling", "bet", new Dictionary<string, object?>
        {
            ["user_id"] = userId, ["chat_id"] = chatId, ["amount"] = amount,
        });

        return new BowlingBetResult(BowlingBetError.None, amount, balance - amount, 0, null, 0, 0);
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
        BotMiniGameSession.ClearCompletedRound(userId, chatId, MiniGameIds.Bowling);
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

    public async Task AbortPendingBetAfterSendDiceFailedAsync(long userId, long chatId, CancellationToken ct)
    {
        var bet = await bets.FindAsync(userId, chatId, ct);
        if (bet == null) return;

        await economics.CreditAsync(userId, chatId, bet.Amount, "bowling.send_dice_failed", ct);
        await bets.DeleteAsync(userId, chatId, ct);
        BotMiniGameSession.ClearCompletedRound(userId, chatId, MiniGameIds.Bowling);
        await telegramDiceRolls.TryRefundRollAsync(userId, chatId, ct);

        analytics.Track("bowling", "bet_aborted", new Dictionary<string, object?>
        {
            ["user_id"] = userId,
            ["chat_id"] = chatId,
            ["amount"] = bet.Amount,
        });
    }
}

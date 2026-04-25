// ─────────────────────────────────────────────────────────────────────────────
// BasketballService — place a 🏀 bet, resolve on throw.
// Telegram basketball dice values 1–5:
//   1-2 = rebound off rim/miss, 3 = bounces off ring, 4 = scored, 5 = clean swish.
// Payout: 4→x2, 5→x2. Uniform 1..5 die ⇒ EV 0.8 of stake.
// ─────────────────────────────────────────────────────────────────────────────

using BotFramework.Host;
using BotFramework.Host.Services;
using BotFramework.Sdk;

namespace Games.Basketball;

public interface IBasketballService
{
    Task<BasketballBetResult> PlaceBetAsync(long userId, string displayName, long chatId, int amount, CancellationToken ct);
    Task<BasketballThrowResult> ThrowAsync(long userId, string displayName, long chatId, int face, CancellationToken ct);
    Task AbortPendingBetAfterSendDiceFailedAsync(long userId, long chatId, CancellationToken ct);
}

public sealed class BasketballService(
    IEconomicsService economics,
    IAnalyticsService analytics,
    IBasketballBetStore bets,
    IDomainEventBus events,
    IRuntimeTuningAccessor tuning,
    IMiniGameSessionGhostHeal ghostHeal,
    ITelegramDiceDailyRollLimiter telegramDiceRolls) : IBasketballService
{
    public static readonly IReadOnlyDictionary<int, int> Multipliers = new Dictionary<int, int>
    {
        [1] = 0, [2] = 0, [3] = 0, [4] = 2, [5] = 2,
    };

    public async Task<BasketballBetResult> PlaceBetAsync(long userId, string displayName, long chatId, int amount, CancellationToken ct)
    {
        var maxBet = tuning.GetSection<BasketballOptions>(BasketballOptions.SectionName).MaxBet;
        if (amount <= 0 || amount > maxBet) return BasketballBetResult.Fail(BasketballBetError.InvalidAmount);

        await economics.EnsureUserAsync(userId, chatId, displayName, ct);
        var balance = await economics.GetBalanceAsync(userId, chatId, ct);
        if (amount > balance) return BasketballBetResult.Fail(BasketballBetError.NotEnoughCoins, balance);

        var session = await BotMiniGamePlaceBetSession.TryBeginWithGhostHealAsync(
            userId,
            chatId,
            MiniGameIds.Basketball,
            async c =>
            {
                if (await bets.FindAsync(userId, chatId, c) == null)
                    BotMiniGameSession.ClearCompletedRound(userId, chatId, MiniGameIds.Basketball);
            },
            ghostHeal,
            ct);
        if (!session.Ok)
            return new BasketballBetResult(BasketballBetError.BusyOtherGame, 0, balance, 0, session.Blocker, 0, 0);

        var existing = await bets.FindAsync(userId, chatId, ct);
        if (existing != null) return BasketballBetResult.Fail(BasketballBetError.AlreadyPending, balance, existing.Amount);

        var gate = await telegramDiceRolls.TryConsumeRollAsync(userId, chatId, ct);
        if (gate.Status == TelegramDiceRollGateStatus.LimitExceeded)
            return new BasketballBetResult(
                BasketballBetError.DailyRollLimit, 0, balance, 0, null, gate.UsedToday, gate.Limit);

        if (!await economics.TryDebitAsync(userId, chatId, amount, "basketball.bet", ct))
        {
            await telegramDiceRolls.TryRefundRollAsync(userId, chatId, ct);
            return BasketballBetResult.Fail(BasketballBetError.NotEnoughCoins, balance);
        }

        if (!await bets.InsertAsync(new BasketballBet(userId, chatId, amount, DateTimeOffset.UtcNow), ct))
        {
            await telegramDiceRolls.TryRefundRollAsync(userId, chatId, ct);
            await economics.CreditAsync(userId, chatId, amount, "basketball.bet.refund", ct);
            return BasketballBetResult.Fail(BasketballBetError.AlreadyPending, balance);
        }

        BotMiniGameSession.RegisterPlacedBet(userId, chatId, MiniGameIds.Basketball);

        analytics.Track("basketball", "bet", new Dictionary<string, object?>
        {
            ["user_id"] = userId, ["chat_id"] = chatId, ["amount"] = amount,
        });

        return new BasketballBetResult(BasketballBetError.None, amount, balance - amount, 0, null, 0, 0);
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
        BotMiniGameSession.ClearCompletedRound(userId, chatId, MiniGameIds.Basketball);
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

    public async Task AbortPendingBetAfterSendDiceFailedAsync(long userId, long chatId, CancellationToken ct)
    {
        var bet = await bets.FindAsync(userId, chatId, ct);
        if (bet == null) return;

        await economics.CreditAsync(userId, chatId, bet.Amount, "basketball.send_dice_failed", ct);
        await bets.DeleteAsync(userId, chatId, ct);
        BotMiniGameSession.ClearCompletedRound(userId, chatId, MiniGameIds.Basketball);
        await telegramDiceRolls.TryRefundRollAsync(userId, chatId, ct);

        analytics.Track("basketball", "bet_aborted", new Dictionary<string, object?>
        {
            ["user_id"] = userId,
            ["chat_id"] = chatId,
            ["amount"] = bet.Amount,
        });
    }
}

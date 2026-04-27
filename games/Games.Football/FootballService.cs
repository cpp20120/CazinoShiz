// ─────────────────────────────────────────────────────────────────────────────
// FootballService — bet, then resolve on Telegram's ⚽ dice (values 1–5).
// Payout: 1–3 → x0, 4 → x2, 5 → x2. Uniform 1..5 ⇒ EV 0.8 of stake.
// ─────────────────────────────────────────────────────────────────────────────

using BotFramework.Host;
using BotFramework.Host.Services;
using BotFramework.Sdk;

namespace Games.Football;

public interface IFootballService
{
    Task<FootballBetResult> PlaceBetAsync(long userId, string displayName, long chatId, int amount, CancellationToken ct);
    Task<FootballThrowResult> ThrowAsync(long userId, string displayName, long chatId, int face, CancellationToken ct);

    /// <summary>Refund and clear pending bet when bot cannot complete SendMessage/SendDice after debit.</summary>
    Task AbortPendingBetAfterSendDiceFailedAsync(long userId, long chatId, CancellationToken ct);
}

public sealed class FootballService(
    IEconomicsService economics,
    IAnalyticsService analytics,
    IFootballBetStore bets,
    IDomainEventBus events,
    IRuntimeTuningAccessor tuning,
    IMiniGameSessionGhostHeal ghostHeal,
    ITelegramDiceDailyRollLimiter telegramDiceRolls,
    IMiniGameSessionStore? sessions = null) : IFootballService
{
    private IMiniGameSessionStore Sessions => sessions ?? NullMiniGameSessionStore.Instance;

    public static readonly IReadOnlyDictionary<int, int> Multipliers = new Dictionary<int, int>
    {
        [1] = 0, [2] = 0, [3] = 0, [4] = 2, [5] = 2,
    };

    public async Task<FootballBetResult> PlaceBetAsync(long userId, string displayName, long chatId, int amount, CancellationToken ct)
    {
        var maxBet = tuning.GetSection<FootballOptions>(FootballOptions.SectionName).MaxBet;
        if (amount <= 0 || amount > maxBet) return FootballBetResult.Fail(FootballBetError.InvalidAmount);

        await economics.EnsureUserAsync(userId, chatId, displayName, ct);
        var balance = await economics.GetBalanceAsync(userId, chatId, ct);
        if (amount > balance) return FootballBetResult.Fail(FootballBetError.NotEnoughCoins, balance);

        var session = await BotMiniGamePlaceBetSession.TryBeginWithGhostHealAsync(
            userId,
            chatId,
            MiniGameIds.Football,
            async c =>
            {
                if (await bets.FindAsync(userId, chatId, c) == null)
                {
                    BotMiniGameSession.ClearCompletedRound(userId, chatId, MiniGameIds.Football);
                    await Sessions.ClearCompletedRoundAsync(userId, chatId, MiniGameIds.Football, c);
                }
            },
            ghostHeal,
            Sessions,
            ct);
        if (!session.Ok)
            return new FootballBetResult(FootballBetError.BusyOtherGame, 0, balance, 0, session.Blocker, 0, 0);

        var existing = await bets.FindAsync(userId, chatId, ct);
        if (existing != null) return FootballBetResult.Fail(FootballBetError.AlreadyPending, balance, existing.Amount);

        var gate = await telegramDiceRolls.TryConsumeRollAsync(userId, chatId, ct);
        if (gate.Status == TelegramDiceRollGateStatus.LimitExceeded)
            return new FootballBetResult(
                FootballBetError.DailyRollLimit, 0, balance, 0, null, gate.UsedToday, gate.Limit);

        if (!await economics.TryDebitAsync(userId, chatId, amount, "football.bet", ct))
        {
            await telegramDiceRolls.TryRefundRollAsync(userId, chatId, ct);
            return FootballBetResult.Fail(FootballBetError.NotEnoughCoins, balance);
        }

        if (!await bets.InsertAsync(new FootballBet(userId, chatId, amount, DateTimeOffset.UtcNow), ct))
        {
            await telegramDiceRolls.TryRefundRollAsync(userId, chatId, ct);
            await economics.CreditAsync(userId, chatId, amount, "football.bet.refund", ct);
            return FootballBetResult.Fail(FootballBetError.AlreadyPending, balance);
        }

        BotMiniGameSession.RegisterPlacedBet(userId, chatId, MiniGameIds.Football);
        await Sessions.RegisterPlacedBetAsync(userId, chatId, MiniGameIds.Football, ct);

        analytics.Track("football", "bet", new Dictionary<string, object?>
        {
            ["user_id"] = userId, ["chat_id"] = chatId, ["amount"] = amount,
        });

        return new FootballBetResult(FootballBetError.None, amount, balance - amount, 0, null, 0, 0);
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
        BotMiniGameSession.ClearCompletedRound(userId, chatId, MiniGameIds.Football);
        await Sessions.ClearCompletedRoundAsync(userId, chatId, MiniGameIds.Football, ct);
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

    public async Task AbortPendingBetAfterSendDiceFailedAsync(long userId, long chatId, CancellationToken ct)
    {
        var bet = await bets.FindAsync(userId, chatId, ct);
        if (bet == null) return;

        await economics.CreditAsync(userId, chatId, bet.Amount, "football.send_dice_failed", ct);
        await bets.DeleteAsync(userId, chatId, ct);
        BotMiniGameSession.ClearCompletedRound(userId, chatId, MiniGameIds.Football);
        await Sessions.ClearCompletedRoundAsync(userId, chatId, MiniGameIds.Football, ct);
        await telegramDiceRolls.TryRefundRollAsync(userId, chatId, ct);

        analytics.Track("football", "bet_aborted", new Dictionary<string, object?>
        {
            ["user_id"] = userId,
            ["chat_id"] = chatId,
            ["amount"] = bet.Amount,
        });
    }
}

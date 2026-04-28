// ─────────────────────────────────────────────────────────────────────────────
// DartsService — place dart bets (queued rounds), resolve on bot 🎯 outcome.
// Payout: 4→x1, 5→x2, 6 (bullseye)→x2. Bot dice is sent per-chat serialized via
// <see cref="DartsRollDispatcherJob"/> + <see cref="DartsBotDiceSender"/>.
// ─────────────────────────────────────────────────────────────────────────────

using BotFramework.Host;
using BotFramework.Host.Services;
using BotFramework.Sdk;

namespace Games.Darts;

public interface IDartsService
{
    Task<DartsBetResult> PlaceBetAsync(
        long userId, string displayName, long chatId, int amount, int replyToMessageId, CancellationToken ct);

    Task<DartsThrowResult> ThrowAsync(
        long roundId, long userId, string displayName, long chatId, int botDiceMessageId, int face,
        CancellationToken ct);

    /// <summary>
    /// Quick-play: atomically place a default bet and settle it with <paramref name="face"/> from
    /// a user-thrown sticker (no bot dice, no queue). Used when the user sends the emoji directly.
    /// </summary>
    Task<DartsThrowResult> QuickThrowAsync(
        long userId, string displayName, long chatId, int face, int amount, CancellationToken ct);

    /// <summary>Refund and remove queued round if we could not send the bet-accepted reply after debit.</summary>
    Task AbortQueuedRoundIfBetReplyFailedAsync(long roundId, long userId, long chatId, CancellationToken ct);
}

public sealed class DartsService(
    IEconomicsService economics,
    IAnalyticsService analytics,
    IDartsRoundStore rounds,
    IMiniGameSessionGhostHeal ghostHeal,
    IDomainEventBus events,
    IDartsRollQueue rollQueue,
    IRuntimeTuningAccessor tuning,
    ITelegramDiceDailyRollLimiter telegramDiceRolls,
    IMiniGameSessionStore? sessions = null) : IDartsService
{
    private IMiniGameSessionStore Sessions => sessions ?? NullMiniGameSessionStore.Instance;

    public static readonly IReadOnlyDictionary<int, int> Multipliers = new Dictionary<int, int>
    {
        [1] = 0, [2] = 0, [3] = 0, [4] = 1, [5] = 2, [6] = 2,
    };

    public async Task<DartsBetResult> PlaceBetAsync(
        long userId, string displayName, long chatId, int amount, int replyToMessageId, CancellationToken ct)
    {
        var maxBet = tuning.GetSection<DartsOptions>(DartsOptions.SectionName).MaxBet;
        if (amount <= 0 || amount > maxBet) return DartsBetResult.Fail(DartsBetError.InvalidAmount);

        await economics.EnsureUserAsync(userId, chatId, displayName, ct);
        var balance = await economics.GetBalanceAsync(userId, chatId, ct);
        if (amount > balance) return DartsBetResult.Fail(DartsBetError.NotEnoughCoins, balance);

        var session = await BotMiniGamePlaceBetSession.TryBeginWithGhostHealAsync(
            userId,
            chatId,
            MiniGameIds.Darts,
            async c =>
            {
                if (await rounds.CountActiveByUserChatAsync(userId, chatId, c) == 0)
                {
                    BotMiniGameSession.ClearCompletedRound(userId, chatId, MiniGameIds.Darts);
                    await Sessions.ClearCompletedRoundAsync(userId, chatId, MiniGameIds.Darts, c);
                }
            },
            ghostHeal,
            Sessions,
            ct);
        if (!session.Ok)
            return new DartsBetResult(DartsBetError.BusyOtherGame, 0, balance, 0, session.Blocker, 0, 0, 0, 0);

        var gate = await telegramDiceRolls.TryConsumeRollAsync(userId, chatId, MiniGameIds.Darts, ct);
        if (gate.Status == TelegramDiceRollGateStatus.LimitExceeded)
            return new DartsBetResult(
                DartsBetError.DailyRollLimit, 0, balance, 0, null, 0, 0, gate.UsedToday, gate.Limit);

        if (!await economics.TryDebitAsync(userId, chatId, amount, "darts.bet", ct))
        {
            await telegramDiceRolls.TryRefundRollAsync(userId, chatId, MiniGameIds.Darts, ct);
            return DartsBetResult.Fail(DartsBetError.NotEnoughCoins, balance);
        }

        long roundId;
        try
        {
            roundId = await rounds.InsertQueuedAsync(
                new DartsRound(
                    0,
                    userId,
                    chatId,
                    amount,
                    DateTimeOffset.UtcNow,
                    DartsRoundStatus.Queued,
                    null,
                    replyToMessageId),
                ct);
        }
        catch
        {
            await telegramDiceRolls.TryRefundRollAsync(userId, chatId, MiniGameIds.Darts, ct);
            await economics.CreditAsync(userId, chatId, amount, "darts.bet.refund", ct);
            throw;
        }

        var queuedAhead = await rounds.CountRollsAheadInChatAsync(chatId, roundId, ct);
        BotMiniGameSession.RegisterPlacedBet(userId, chatId, MiniGameIds.Darts);
        await Sessions.RegisterPlacedBetAsync(userId, chatId, MiniGameIds.Darts, ct);

        rollQueue.Enqueue(new DartsRollJob(roundId, chatId, userId, displayName, replyToMessageId));

        analytics.Track("darts", "bet", new Dictionary<string, object?>
        {
            ["user_id"] = userId, ["chat_id"] = chatId, ["amount"] = amount, ["round_id"] = roundId,
        });

        return new DartsBetResult(
            DartsBetError.None, amount, balance - amount, 0, null, roundId, queuedAhead, 0, 0);
    }

    public async Task<DartsThrowResult> ThrowAsync(
        long roundId, long userId, string displayName, long chatId, int botDiceMessageId, int face,
        CancellationToken ct)
    {
        var bet = await rounds.FindByIdAsync(roundId, ct);
        if (bet is not { Status: DartsRoundStatus.AwaitingOutcome }
            || bet.UserId != userId
            || bet.ChatId != chatId
            || bet.BotMessageId != botDiceMessageId)
            return new DartsThrowResult(DartsThrowOutcome.NoBet);

        await economics.EnsureUserAsync(userId, chatId, displayName, ct);
        var multiplier = Multipliers.TryGetValue(face, out var m) ? m : 0;
        var payout = bet.Amount * multiplier;

        if (payout > 0)
            await economics.CreditAsync(userId, chatId, payout, "darts.payout", ct);

        await rounds.DeleteAsync(roundId, ct);

        var remaining = await rounds.CountActiveByUserChatAsync(userId, chatId, ct);
        if (remaining == 0)
        {
            BotMiniGameRollGate.Clear("darts", userId, chatId);
            BotMiniGameSession.ClearCompletedRound(userId, chatId, MiniGameIds.Darts);
            await Sessions.ClearCompletedRoundAsync(userId, chatId, MiniGameIds.Darts, ct);
        }

        var balance = await economics.GetBalanceAsync(userId, chatId, ct);

        analytics.Track("darts", "throw", new Dictionary<string, object?>
        {
            ["user_id"] = userId, ["chat_id"] = chatId, ["face"] = face,
            ["bet"] = bet.Amount, ["multiplier"] = multiplier, ["payout"] = payout, ["round_id"] = roundId,
        });

        var darts = tuning.GetSection<DartsOptions>(DartsOptions.SectionName);
        var occurredAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await events.PublishAsync(
            new DartsThrowCompleted(userId, chatId, face, bet.Amount, multiplier, payout, occurredAt),
            ct);
        await TelegramMiniGameRedeemDrops.MaybePublishAsync(
            events, darts.RedeemDropChance, userId, chatId, MiniGameIds.Darts, occurredAt, ct);

        return new DartsThrowResult(DartsThrowOutcome.Thrown, face, bet.Amount, multiplier, payout, balance);
    }

    public async Task AbortQueuedRoundIfBetReplyFailedAsync(long roundId, long userId, long chatId, CancellationToken ct)
    {
        var row = await rounds.FindByIdAsync(roundId, ct);
        if (row is not { Status: DartsRoundStatus.Queued } || row.UserId != userId || row.ChatId != chatId)
            return;

        await telegramDiceRolls.TryRefundRollAsync(userId, chatId, MiniGameIds.Darts, ct);
        await economics.CreditAsync(userId, chatId, row.Amount, "darts.bet_reply_failed.refund", ct);
        await rounds.DeleteAsync(roundId, ct);

        var remaining = await rounds.CountActiveByUserChatAsync(userId, chatId, ct);
        if (remaining == 0)
        {
            BotMiniGameRollGate.Clear("darts", userId, chatId);
            BotMiniGameSession.ClearCompletedRound(userId, chatId, MiniGameIds.Darts);
            await Sessions.ClearCompletedRoundAsync(userId, chatId, MiniGameIds.Darts, ct);
        }
    }

    /// <summary>
    /// Quick-play path: place a bet for <paramref name="amount"/> and immediately settle it using
    /// the face value from the user's own thrown sticker.  No queue, no bot dice involved.
    /// </summary>
    public async Task<DartsThrowResult> QuickThrowAsync(
        long userId, string displayName, long chatId, int face, int amount, CancellationToken ct)
    {
        var maxBet = tuning.GetSection<DartsOptions>(DartsOptions.SectionName).MaxBet;
        if (amount <= 0 || amount > maxBet)
            return new DartsThrowResult(DartsThrowOutcome.BetInvalid);

        await economics.EnsureUserAsync(userId, chatId, displayName, ct);
        var balance = await economics.GetBalanceAsync(userId, chatId, ct);
        if (amount > balance)
            return new DartsThrowResult(DartsThrowOutcome.BetNotEnoughCoins, Balance: balance);

        var session = await BotMiniGamePlaceBetSession.TryBeginWithGhostHealAsync(
            userId, chatId, MiniGameIds.Darts,
            async c =>
            {
                if (await rounds.CountActiveByUserChatAsync(userId, chatId, c) == 0)
                {
                    BotMiniGameSession.ClearCompletedRound(userId, chatId, MiniGameIds.Darts);
                    await Sessions.ClearCompletedRoundAsync(userId, chatId, MiniGameIds.Darts, c);
                }
            },
            ghostHeal, Sessions, ct);
        if (!session.Ok)
            return new DartsThrowResult(DartsThrowOutcome.BetBusyOtherGame, BlockingGameId: session.Blocker);

        var gate = await telegramDiceRolls.TryConsumeRollAsync(userId, chatId, MiniGameIds.Darts, ct);
        if (gate.Status == TelegramDiceRollGateStatus.LimitExceeded)
            return new DartsThrowResult(
                DartsThrowOutcome.BetDailyLimit,
                DailyRollUsed: gate.UsedToday,
                DailyRollLimit: gate.Limit);

        if (!await economics.TryDebitAsync(userId, chatId, amount, "darts.quickplay.bet", ct))
        {
            await telegramDiceRolls.TryRefundRollAsync(userId, chatId, MiniGameIds.Darts, ct);
            return new DartsThrowResult(DartsThrowOutcome.BetNotEnoughCoins, Balance: balance);
        }

        // Settle immediately — the user's own dice IS the throw.
        var multiplier = Multipliers.TryGetValue(face, out var m) ? m : 0;
        var payout = amount * multiplier;

        if (payout > 0)
            await economics.CreditAsync(userId, chatId, payout, "darts.quickplay.payout", ct);

        BotMiniGameSession.ClearCompletedRound(userId, chatId, MiniGameIds.Darts);
        await Sessions.ClearCompletedRoundAsync(userId, chatId, MiniGameIds.Darts, ct);
        var newBalance = await economics.GetBalanceAsync(userId, chatId, ct);

        analytics.Track("darts", "quickplay", new Dictionary<string, object?>
        {
            ["user_id"] = userId, ["chat_id"] = chatId, ["face"] = face,
            ["bet"] = amount, ["multiplier"] = multiplier, ["payout"] = payout,
        });

        var darts = tuning.GetSection<DartsOptions>(DartsOptions.SectionName);
        var occurredAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await events.PublishAsync(
            new DartsThrowCompleted(userId, chatId, face, amount, multiplier, payout, occurredAt),
            ct);
        await TelegramMiniGameRedeemDrops.MaybePublishAsync(
            events, darts.RedeemDropChance, userId, chatId, MiniGameIds.Darts, occurredAt, ct);

        return new DartsThrowResult(DartsThrowOutcome.Thrown, face, amount, multiplier, payout, newBalance);
    }
}

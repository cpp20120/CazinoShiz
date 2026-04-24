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
}

public sealed class DartsService(
    IEconomicsService economics,
    IAnalyticsService analytics,
    IDartsRoundStore rounds,
    IMiniGameSessionGhostHeal ghostHeal,
    IDomainEventBus events,
    IDartsRollQueue rollQueue,
    IRuntimeTuningAccessor tuning,
    ITelegramDiceDailyRollLimiter telegramDiceRolls) : IDartsService
{
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
                    BotMiniGameSession.ClearCompletedRound(userId, chatId, MiniGameIds.Darts);
            },
            ghostHeal,
            ct);
        if (!session.Ok)
            return new DartsBetResult(DartsBetError.BusyOtherGame, 0, balance, 0, session.Blocker, 0, 0, 0, 0);

        var gate = await telegramDiceRolls.TryConsumeRollAsync(userId, chatId, ct);
        if (gate.Status == TelegramDiceRollGateStatus.LimitExceeded)
            return new DartsBetResult(
                DartsBetError.DailyRollLimit, 0, balance, 0, null, 0, 0, gate.UsedToday, gate.Limit);

        if (!await economics.TryDebitAsync(userId, chatId, amount, "darts.bet", ct))
        {
            await telegramDiceRolls.TryRefundRollAsync(userId, chatId, ct);
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
            await telegramDiceRolls.TryRefundRollAsync(userId, chatId, ct);
            await economics.CreditAsync(userId, chatId, amount, "darts.bet.refund", ct);
            throw;
        }

        var queuedAhead = await rounds.CountRollsAheadInChatAsync(chatId, roundId, ct);
        BotMiniGameSession.RegisterPlacedBet(userId, chatId, MiniGameIds.Darts);

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
        }

        var balance = await economics.GetBalanceAsync(userId, chatId, ct);

        analytics.Track("darts", "throw", new Dictionary<string, object?>
        {
            ["user_id"] = userId, ["chat_id"] = chatId, ["face"] = face,
            ["bet"] = bet.Amount, ["multiplier"] = multiplier, ["payout"] = payout, ["round_id"] = roundId,
        });

        await events.PublishAsync(
            new DartsThrowCompleted(userId, chatId, face, bet.Amount, multiplier, payout,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()),
            ct);

        return new DartsThrowResult(DartsThrowOutcome.Thrown, face, bet.Amount, multiplier, payout, balance);
    }
}

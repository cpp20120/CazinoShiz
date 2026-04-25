// ─────────────────────────────────────────────────────────────────────────────
// DiceHandler — MessageDice("🎰") handler. Framework's UpdateRouter discovers
// this via the [MessageDice] attribute and dispatches every 🎰 from a user.
//
// Stores pending bets to enable recovery if SendMessage fails. When a 🎰 is
// received:
//   1. Check for an existing pending bet
//   2. If found: resolve it (compute win, credit payout, send result)
//   3. If not found: place a new bet (debit stake, store bet, resolve immediately)
//
// If PlaceBetAsync succeeds but ResolveBetAsync throws, the handler calls
// AbortBetAfterSendDiceFailedAsync to refund the pending stake. After a full
// resolve, the bet row is removed and the round is final; a failed result
// SendMessage only affects the chat copy — do not call Abort (nothing pending).
// ─────────────────────────────────────────────────────────────────────────────

using BotFramework.Host;
using BotFramework.Sdk;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Games.Dice;

[MessageDice("🎰")]
public sealed partial class DiceHandler(
    IDiceService service,
    ILocalizer localizer,
    ILogger<DiceHandler> logger) : IUpdateHandler
{
    public async Task HandleAsync(UpdateContext ctx)
    {
        var msg = ctx.MessageOrEdited;
        if (msg?.Dice?.Emoji != "🎰") return;
        if (msg.Dice is not { Value: > 0 }) return;

        var dice = msg.Dice!;
        var userId = msg.From?.Id ?? 0;
        if (userId == 0 || msg.From?.IsBot == true) return;

        var chatId = msg.Chat.Id;
        var reply = new ReplyParameters { MessageId = msg.MessageId };
        var displayName = msg.From?.Username ?? msg.From?.FirstName ?? $"User ID: {userId}";

        // Try to resolve an existing pending bet first
        try
        {
            var resolveResult = await service.ResolveBetAsync(userId, displayName, chatId, ctx.Ct);
            if (resolveResult.Outcome != DiceOutcome.NoPendingBet)
            {
                // Bet was resolved successfully
                var net = resolveResult.Prize - resolveResult.Loss;
                var isWin = net > 0;
                var text = string.Join("\n",
                [
                    isWin
                        ? string.Format(Loc("result.win"), resolveResult.Prize, resolveResult.Loss, net)
                        : string.Format(Loc("result.lose"), resolveResult.Loss, resolveResult.Prize, -net),
                    string.Format(Loc("result.balance"), resolveResult.NewBalance),
                ]);

                try
                {
                    await ctx.Bot.SendMessage(chatId, text,
                        parseMode: ParseMode.Html, replyParameters: reply, cancellationToken: ctx.Ct);
                }
                catch (Exception ex)
                {
                    LogReplyFailed(userId, ex);
                }
                return;
            }
        }
        catch (Exception ex)
        {
            LogResolveFailed(userId, ex);
            return;
        }

        // No pending bet; place a new one
        try
        {
            var placeBetResult = await service.PlaceBetAsync(
                userId, displayName, dice.Value, chatId,
                isForwarded: msg.ForwardOrigin != null,
                ctx.Ct);

            switch (placeBetResult.Outcome)
            {
                case DiceOutcome.Forwarded:
                    await ctx.Bot.SendMessage(chatId, Loc("err.forwarded"),
                        replyParameters: reply, cancellationToken: ctx.Ct);
                    return;

                case DiceOutcome.NotEnoughCoins:
                    await ctx.Bot.SendMessage(
                        chatId,
                        string.Format(Loc("err.not_enough_coins"), placeBetResult.Loss),
                        replyParameters: reply, cancellationToken: ctx.Ct);
                    return;

                case DiceOutcome.DailyRollLimitExceeded:
                    await ctx.Bot.SendMessage(
                        chatId,
                        string.Format(Loc("err.daily_roll_limit"), placeBetResult.DailyDiceUsed, placeBetResult.DailyDiceLimit),
                        parseMode: ParseMode.Html, replyParameters: reply, cancellationToken: ctx.Ct);
                    return;

                case DiceOutcome.BetStoreError:
                    await ctx.Bot.SendMessage(chatId, Loc("err.bet_store"),
                        replyParameters: reply, cancellationToken: ctx.Ct);
                    return;
            }

            // Bet placed successfully; resolve it immediately
            try
            {
                var resolveResult = await service.ResolveBetAsync(userId, displayName, chatId, ctx.Ct);

                var net = resolveResult.Prize - resolveResult.Loss;
                var isWin = net > 0;
                var text = string.Join("\n",
                [
                    isWin
                        ? string.Format(Loc("result.win"), resolveResult.Prize, resolveResult.Loss, net)
                        : string.Format(Loc("result.lose"), resolveResult.Loss, resolveResult.Prize, -net),
                    string.Format(Loc("result.balance"), resolveResult.NewBalance),
                    placeBetResult.Gas > 0 ? string.Format(Loc("result.gas"), placeBetResult.Gas) : "",
                ]);

                try
                {
                    await ctx.Bot.SendMessage(chatId, text,
                        parseMode: ParseMode.Html, replyParameters: reply, cancellationToken: ctx.Ct);
                }
                catch (Exception ex)
                {
                   LogReplyFailed(userId, ex);
                }
            }
            catch (Exception ex)
            {
                LogResolveFailed(userId, ex);
                try
                {
                    await service.AbortBetAfterSendDiceFailedAsync(userId, chatId, ctx.Ct);
                }
                catch (Exception abortEx)
                {
                    LogAbortFailed(userId, abortEx);
                }
            }
        }
        catch (Exception ex)
        {
            LogBetPlacementFailed(userId, ex);
        }
    }

    private string Loc(string key) => localizer.Get("dice", key);

    [LoggerMessage(EventId = 2001, Level = LogLevel.Error, Message = "dice.reply.failed user={UserId}")]
    partial void LogReplyFailed(long userId, Exception exception);

    [LoggerMessage(EventId = 2002, Level = LogLevel.Error, Message = "dice.resolve.failed user={UserId}")]
    partial void LogResolveFailed(long userId, Exception exception);

    [LoggerMessage(EventId = 2003, Level = LogLevel.Error, Message = "dice.bet_placement.failed user={UserId}")]
    partial void LogBetPlacementFailed(long userId, Exception exception);

    [LoggerMessage(EventId = 2004, Level = LogLevel.Error, Message = "dice.abort.failed user={UserId}")]
    partial void LogAbortFailed(long userId, Exception exception);
}



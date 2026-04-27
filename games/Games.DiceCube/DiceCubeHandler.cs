// ─────────────────────────────────────────────────────────────────────────────
// DiceCubeHandler — handles both /dice text commands and raw 🎲 throws. The
// framework's UpdateRouter dispatches by attribute; the handler routes
// internally on Update shape (Dice message vs text command).
// After a successful /dice bet the bot sends 🎲. User throws are ignored until the
// bot roll is processed or the gate TTL expires (then one manual throw is OK if SendDice failed).
// ─────────────────────────────────────────────────────────────────────────────

using BotFramework.Host;
using BotFramework.Host.Services;
using BotFramework.Sdk;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Games.DiceCube;

[Command("/dice")]
[MessageDice("🎲")]
public sealed partial class DiceCubeHandler(
    IDiceCubeService service,
    IRuntimeTuningAccessor tuning,
    ILocalizer localizer,
    ILogger<DiceCubeHandler> logger) : IUpdateHandler
{
    private const string DiceEmoji = "🎲";
    private const string RollGateId = "dicecube";

    public async Task HandleAsync(UpdateContext ctx)
    {
        var diceMsg = ctx.MessageOrEdited;
        if (diceMsg?.Dice?.Emoji == DiceEmoji)
        {
            if (!BotMiniGameDiceOwner.TryResolveDicePlayer(diceMsg, out var uid, out var dname))
                return;
            if (diceMsg.From is { IsBot: false }
                && BotMiniGameRollGate.ShouldIgnoreUserThrow(RollGateId, uid, diceMsg.Chat.Id))
            {
                await ctx.Bot.SendMessage(diceMsg.Chat.Id, Loc("roll.wait_bot"),
                    parseMode: ParseMode.Html,
                    replyParameters: new ReplyParameters { MessageId = diceMsg.MessageId },
                    cancellationToken: ctx.Ct);
                return;
            }

            var diceReply = new ReplyParameters { MessageId = diceMsg.MessageId };
            await HandleRollAsync(ctx, diceMsg, uid, dname, diceMsg.Chat.Id, diceReply);
            return;
        }

        var msg = ctx.Update.Message;
        if (msg?.Text == null) return;

        var userId = msg.From?.Id ?? 0;
        if (userId == 0) return;

        var chatId = msg.Chat.Id;
        var reply = new ReplyParameters { MessageId = msg.MessageId };
        var displayName = msg.From?.Username ?? msg.From?.FirstName ?? $"User ID: {userId}";

        var parts = msg.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var action = parts.Length > 1 ? parts[1] : "";

        switch (action)
        {
            case "help":
                await ctx.Bot.SendMessage(chatId,
                    string.Format(Loc("usage"), tuning.GetSection<DiceCubeOptions>(DiceCubeOptions.SectionName).DefaultBet),
                    parseMode: ParseMode.Html, replyParameters: reply, cancellationToken: ctx.Ct);
                break;
            case "bet":
            case "":
                await HandleBetAsync(ctx, userId, displayName, chatId, parts, reply);
                break;
            default:
                await ctx.Bot.SendMessage(chatId,
                    string.Format(Loc("usage"), tuning.GetSection<DiceCubeOptions>(DiceCubeOptions.SectionName).DefaultBet),
                    parseMode: ParseMode.Html, replyParameters: reply, cancellationToken: ctx.Ct);
                break;
        }
    }

    private async Task HandleBetAsync(UpdateContext ctx, long userId, string displayName, long chatId,
        string[] parts, ReplyParameters reply)
    {
        int amount;
        if (parts.Length == 1)
            amount = tuning.GetSection<DiceCubeOptions>(DiceCubeOptions.SectionName).DefaultBet;
        else if (parts.Length == 2)
        {
            if (!parts[1].Equals("bet", StringComparison.OrdinalIgnoreCase))
            {
                await ctx.Bot.SendMessage(chatId,
                    string.Format(Loc("bet.usage"), tuning.GetSection<DiceCubeOptions>(DiceCubeOptions.SectionName).DefaultBet),
                    parseMode: ParseMode.Html, replyParameters: reply, cancellationToken: ctx.Ct);
                return;
            }

            amount = tuning.GetSection<DiceCubeOptions>(DiceCubeOptions.SectionName).DefaultBet;
        }
        else if (parts.Length >= 3
            && parts[1].Equals("bet", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(parts[2], out amount)) { }
        else
        {
            await ctx.Bot.SendMessage(chatId,
                string.Format(Loc("bet.usage"), tuning.GetSection<DiceCubeOptions>(DiceCubeOptions.SectionName).DefaultBet),
                parseMode: ParseMode.Html, replyParameters: reply, cancellationToken: ctx.Ct);
            return;
        }

        var r = await service.PlaceBetAsync(userId, displayName, chatId, amount, ctx.Ct);
        var text = r.Error switch
        {
            CubeBetError.None => string.Format(
                Loc("bet.accepted"), r.Amount, service.Mult4, service.Mult5, service.Mult6),
            CubeBetError.InvalidAmount => Loc("bet.invalid"),
            CubeBetError.NotEnoughCoins => string.Format(Loc("bet.not_enough"), r.Balance),
            CubeBetError.AlreadyPending => string.Format(Loc("bet.already_pending"), r.PendingAmount),
            CubeBetError.BusyOtherGame => string.Format(Loc("bet.busy_other"), MiniGameLabels.Ru(r.BlockingGameId!)),
            CubeBetError.Cooldown => string.Format(Loc("bet.cooldown"), r.CooldownSeconds),
            CubeBetError.DailyRollLimit => string.Format(Loc("bet.daily_roll_limit"), r.DailyRollUsed, r.DailyRollLimit),
            _ => Loc("bet.failed"),
        };
        try
        {
            await ctx.Bot.SendMessage(chatId, text, replyParameters: reply, cancellationToken: ctx.Ct);
        }
        catch (Exception ex)
        {
            LogReplyFailed(userId, ex);
            if (r.Error == CubeBetError.None)
            {
                try
                {
                    await service.AbortPendingBetAfterSendDiceFailedAsync(userId, chatId, ctx.Ct);
                }
                catch (Exception abortEx)
                {
                    LogAbortAfterBotDiceFailed(userId, abortEx);
                }
            }

            return;
        }

        if (r.Error == CubeBetError.None)
        {
            BotMiniGameRollGate.ExpectBotRoll(RollGateId, userId, chatId);
            try
            {
                var diceSent = await ctx.Bot.SendDice(chatId, emoji: DiceEmoji, replyParameters: reply,
                    cancellationToken: ctx.Ct);
                if (diceSent.Dice is { Value: > 0 })
                {
                    var diceReply = new ReplyParameters { MessageId = diceSent.MessageId };
                    await HandleRollAsync(ctx, diceSent, userId, displayName, chatId, diceReply);
                }
                else
                    BotMiniGameDiceOwner.Bind(chatId, diceSent.MessageId, userId, displayName);
            }
            catch (Exception ex)
            {
                BotMiniGameRollGate.Clear(RollGateId, userId, chatId);
                try
                {
                    await service.AbortPendingBetAfterSendDiceFailedAsync(userId, chatId, ctx.Ct);
                }
                catch (Exception abortEx)
                {
                    LogAbortAfterBotDiceFailed(userId, abortEx);
                }

                LogBotDiceFailed(userId, ex);
            }
        }
    }

    private async Task HandleRollAsync(UpdateContext ctx, Message msg, long userId, string displayName,
        long chatId, ReplyParameters reply)
    {
        if (msg.Dice is not { Value: > 0 }) return;

        try
        {
            var face = msg.Dice!.Value;
            var r = await service.RollAsync(userId, displayName, chatId, face, ctx.Ct);

            if (r.Outcome == CubeRollOutcome.NoBet)
            {
                // Bot dice with no bet → already settled or bad state, skip silently.
                if (msg.From is { IsBot: true }) return;

                // User threw 🎲 without a prior /dice command → quick-play with default bet.
                var defaultBet = tuning.GetSection<DiceCubeOptions>(DiceCubeOptions.SectionName).DefaultBet;
                var betR = await service.PlaceBetAsync(userId, displayName, chatId, defaultBet, ctx.Ct);
                if (betR.Error != CubeBetError.None)
                {
                    var errText = betR.Error switch
                    {
                        CubeBetError.InvalidAmount  => Loc("bet.invalid"),
                        CubeBetError.NotEnoughCoins => string.Format(Loc("bet.not_enough"), betR.Balance),
                        CubeBetError.AlreadyPending => string.Format(Loc("bet.already_pending"), betR.PendingAmount),
                        CubeBetError.BusyOtherGame  => string.Format(Loc("bet.busy_other"), MiniGameLabels.Ru(betR.BlockingGameId!)),
                        CubeBetError.Cooldown       => string.Format(Loc("bet.cooldown"), betR.CooldownSeconds),
                        CubeBetError.DailyRollLimit => string.Format(Loc("bet.daily_roll_limit"), betR.DailyRollUsed, betR.DailyRollLimit),
                        _                           => Loc("bet.failed"),
                    };
                    await ctx.Bot.SendMessage(chatId, errText, parseMode: ParseMode.Html,
                        replyParameters: reply, cancellationToken: ctx.Ct);
                    return;
                }

                // Bet placed — immediately settle using the already-known face value.
                await ctx.Bot.SendMessage(chatId, Loc("roll.quick_wait"), parseMode: ParseMode.Html,
                    replyParameters: reply, cancellationToken: ctx.Ct);
                r = await service.RollAsync(userId, displayName, chatId, face, ctx.Ct);
            }

            var net = r.Payout - r.Bet;
            var text = r.Payout > 0
                ? string.Format(Loc("roll.win"), r.Face, r.Multiplier, r.Bet, r.Payout, net, r.Balance)
                : string.Format(Loc("roll.lose"), r.Face, r.Bet, r.Balance);
            try
            {
                await Task.Delay(4000, ctx.Ct);
                await ctx.Bot.SendMessage(chatId, text,
                    parseMode: ParseMode.Html, replyParameters: reply, cancellationToken: ctx.Ct);
            }
            catch (Exception ex) { LogReplyFailed(userId, ex); }
        }
        finally
        {
            BotMiniGameRollGate.Clear(RollGateId, userId, chatId);
            if (msg.From is { IsBot: true })
                BotMiniGameDiceOwner.MarkCompleted(chatId, msg.MessageId);
            else
                BotMiniGameDiceOwner.Unbind(chatId, msg.MessageId);
        }
    }

    private string Loc(string key) => localizer.Get("dicecube", key);

    [LoggerMessage(EventId = 2101, Level = LogLevel.Error, Message = "dicecube.reply.failed user={UserId}")]
    partial void LogReplyFailed(long userId, Exception exception);

    [LoggerMessage(EventId = 2102, Level = LogLevel.Warning, Message = "dicecube.bot_dice.failed user={UserId}")]
    partial void LogBotDiceFailed(long userId, Exception exception);

    [LoggerMessage(EventId = 2103, Level = LogLevel.Error, Message = "dicecube.abort_after_send_failed user={UserId}")]
    partial void LogAbortAfterBotDiceFailed(long userId, Exception exception);
}

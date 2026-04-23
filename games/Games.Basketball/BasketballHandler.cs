using BotFramework.Host;
using BotFramework.Sdk;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Games.Basketball;

[Command("/basket")]
[MessageDice("🏀")]
public sealed partial class BasketballHandler(
    IBasketballService service,
    IOptions<BasketballOptions> options,
    ILocalizer localizer,
    ILogger<BasketballHandler> logger) : IUpdateHandler
{
    private const string DiceEmoji = "🏀";
    private const string RollGateId = "basketball";

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
            await HandleThrowAsync(ctx, diceMsg, uid, dname, diceMsg.Chat.Id, diceReply);
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
                    string.Format(Loc("usage"), options.Value.DefaultBet),
                    parseMode: ParseMode.Html, replyParameters: reply, cancellationToken: ctx.Ct);
                break;
            case "bet":
            case "":
                await HandleBetAsync(ctx, userId, displayName, chatId, parts, reply);
                break;
            default:
                await ctx.Bot.SendMessage(chatId,
                    string.Format(Loc("usage"), options.Value.DefaultBet),
                    parseMode: ParseMode.Html, replyParameters: reply, cancellationToken: ctx.Ct);
                break;
        }
    }

    private async Task HandleBetAsync(UpdateContext ctx, long userId, string displayName, long chatId,
        string[] parts, ReplyParameters reply)
    {
        int amount;
        if (parts.Length == 1)
            amount = options.Value.DefaultBet;
        else if (parts.Length == 2)
        {
            if (!parts[1].Equals("bet", StringComparison.OrdinalIgnoreCase))
            {
                await ctx.Bot.SendMessage(chatId,
                    string.Format(Loc("bet.usage"), options.Value.DefaultBet),
                    parseMode: ParseMode.Html, replyParameters: reply, cancellationToken: ctx.Ct);
                return;
            }

            amount = options.Value.DefaultBet;
        }
        else if (parts.Length >= 3
            && parts[1].Equals("bet", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(parts[2], out amount)) { }
        else
        {
            await ctx.Bot.SendMessage(chatId,
                string.Format(Loc("bet.usage"), options.Value.DefaultBet),
                parseMode: ParseMode.Html, replyParameters: reply, cancellationToken: ctx.Ct);
            return;
        }

        var r = await service.PlaceBetAsync(userId, displayName, chatId, amount, ctx.Ct);
        var text = r.Error switch
        {
            BasketballBetError.None => string.Format(Loc("bet.accepted"), r.Amount),
            BasketballBetError.InvalidAmount => Loc("bet.invalid"),
            BasketballBetError.NotEnoughCoins => string.Format(Loc("bet.not_enough"), r.Balance),
            BasketballBetError.AlreadyPending => string.Format(Loc("bet.already_pending"), r.PendingAmount),
            BasketballBetError.BusyOtherGame => string.Format(Loc("bet.busy_other"), MiniGameLabels.Ru(r.BlockingGameId!)),
            _ => Loc("bet.failed"),
        };
        try
        {
            await ctx.Bot.SendMessage(chatId, text, replyParameters: reply, cancellationToken: ctx.Ct);
        }
        catch (Exception ex) { LogReplyFailed(userId, ex); return; }

        if (r.Error == BasketballBetError.None)
        {
            BotMiniGameRollGate.ExpectBotRoll(RollGateId, userId, chatId);
            try
            {
                var diceSent = await ctx.Bot.SendDice(chatId, emoji: DiceEmoji, replyParameters: reply,
                    cancellationToken: ctx.Ct);
                BotMiniGameDiceOwner.Bind(chatId, diceSent.MessageId, userId, displayName);
            }
            catch (Exception ex)
            {
                BotMiniGameRollGate.Clear(RollGateId, userId, chatId);
                LogBotDiceFailed(userId, ex);
            }
        }
    }

    private async Task HandleThrowAsync(UpdateContext ctx, Message msg, long userId, string displayName,
        long chatId, ReplyParameters reply)
    {
        if (msg.Dice is not { Value: > 0 }) return;

        try
        {
            var face = msg.Dice!.Value;
            var r = await service.ThrowAsync(userId, displayName, chatId, face, ctx.Ct);

            if (r.Outcome == BasketballThrowOutcome.NoBet)
            {
                await ctx.Bot.SendMessage(chatId, Loc("throw.no_bet"),
                    parseMode: ParseMode.Html, replyParameters: reply, cancellationToken: ctx.Ct);
                return;
            }

            var net = r.Payout - r.Bet;
            var text = r.Payout > 0
                ? string.Format(Loc("throw.win"), r.Face, r.Multiplier, r.Bet, r.Payout, net, r.Balance)
                : string.Format(Loc("throw.lose"), r.Face, r.Bet, r.Balance);
            try
            {
                await ctx.Bot.SendMessage(chatId, text,
                    parseMode: ParseMode.Html, replyParameters: reply, cancellationToken: ctx.Ct);
            }
            catch (Exception ex) { LogReplyFailed(userId, ex); }
        }
        finally
        {
            BotMiniGameRollGate.Clear(RollGateId, userId, chatId);
            BotMiniGameDiceOwner.Unbind(chatId, msg.MessageId);
        }
    }

    private string Loc(string key) => localizer.Get("basketball", key);

    [LoggerMessage(EventId = 2301, Level = LogLevel.Error, Message = "basketball.reply.failed user={UserId}")]
    partial void LogReplyFailed(long userId, Exception exception);

    [LoggerMessage(EventId = 2302, Level = LogLevel.Warning, Message = "basketball.bot_dice.failed user={UserId}")]
    partial void LogBotDiceFailed(long userId, Exception exception);
}

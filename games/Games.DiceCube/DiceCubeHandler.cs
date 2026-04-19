// ─────────────────────────────────────────────────────────────────────────────
// DiceCubeHandler — handles both /dice text commands and raw 🎲 throws. The
// framework's UpdateRouter dispatches by attribute; the handler routes
// internally on Update shape (Dice message vs text command).
// ─────────────────────────────────────────────────────────────────────────────

using BotFramework.Host;
using BotFramework.Sdk;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Games.DiceCube;

[Command("/dice")]
[MessageDice("🎲")]
public sealed partial class DiceCubeHandler(
    IDiceCubeService service,
    ILocalizer localizer,
    ILogger<DiceCubeHandler> logger) : IUpdateHandler
{
    public async Task HandleAsync(UpdateContext ctx)
    {
        var msg = ctx.Update.Message!;
        var userId = msg.From?.Id ?? 0;
        if (userId == 0) return;

        var chatId = msg.Chat.Id;
        var reply = new ReplyParameters { MessageId = msg.MessageId };
        var displayName = msg.From?.Username ?? msg.From?.FirstName ?? $"User ID: {userId}";

        if (msg.Dice?.Emoji == "🎲")
        {
            await HandleRollAsync(ctx, msg, userId, displayName, chatId, reply);
            return;
        }

        var parts = (msg.Text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var action = parts.Length > 1 ? parts[1] : "";

        switch (action)
        {
            case "bet": await HandleBetAsync(ctx, userId, displayName, chatId, parts, reply); break;
            default:
                await ctx.Bot.SendMessage(chatId, Loc("usage"),
                    parseMode: ParseMode.Html, replyParameters: reply, cancellationToken: ctx.Ct);
                break;
        }
    }

    private async Task HandleBetAsync(UpdateContext ctx, long userId, string displayName, long chatId,
        string[] parts, ReplyParameters reply)
    {
        if (parts.Length < 3 || !int.TryParse(parts[2], out var amount))
        {
            await ctx.Bot.SendMessage(chatId, Loc("bet.usage"),
                parseMode: ParseMode.Html, replyParameters: reply, cancellationToken: ctx.Ct);
            return;
        }

        var r = await service.PlaceBetAsync(userId, displayName, chatId, amount, ctx.Ct);
        var text = r.Error switch
        {
            CubeBetError.None => string.Format(Loc("bet.accepted"), r.Amount),
            CubeBetError.InvalidAmount => Loc("bet.invalid"),
            CubeBetError.NotEnoughCoins => string.Format(Loc("bet.not_enough"), r.Balance),
            CubeBetError.AlreadyPending => string.Format(Loc("bet.already_pending"), r.PendingAmount),
            _ => Loc("bet.failed"),
        };
        try
        {
            await ctx.Bot.SendMessage(chatId, text, replyParameters: reply, cancellationToken: ctx.Ct);
        }
        catch (Exception ex) { LogReplyFailed(userId, ex); }
    }

    private async Task HandleRollAsync(UpdateContext ctx, Message msg, long userId, string displayName,
        long chatId, ReplyParameters reply)
    {
        var face = msg.Dice!.Value;
        var r = await service.RollAsync(userId, displayName, chatId, face, ctx.Ct);

        if (r.Outcome == CubeRollOutcome.NoBet)
        {
            await ctx.Bot.SendMessage(chatId, Loc("roll.no_bet"),
                parseMode: ParseMode.Html, replyParameters: reply, cancellationToken: ctx.Ct);
            return;
        }

        var text = r.Payout > 0
            ? string.Format(Loc("roll.win"), r.Face, r.Multiplier, r.Payout, r.Balance)
            : string.Format(Loc("roll.lose"), r.Face, r.Bet, r.Balance);
        try
        {
            await ctx.Bot.SendMessage(chatId, text,
                parseMode: ParseMode.Html, replyParameters: reply, cancellationToken: ctx.Ct);
        }
        catch (Exception ex) { LogReplyFailed(userId, ex); }
    }

    private string Loc(string key) => localizer.Get("dicecube", key);

    [LoggerMessage(EventId = 2101, Level = LogLevel.Error, Message = "dicecube.reply.failed user={UserId}")]
    partial void LogReplyFailed(long userId, Exception exception);
}

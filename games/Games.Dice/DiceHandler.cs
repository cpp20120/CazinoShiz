// ─────────────────────────────────────────────────────────────────────────────
// DiceHandler — handles both slot commands and raw 🎰 throws.
//
// Commands send a bot 🎰 and settle it for the command author. Raw user 🎰 messages
// are still supported and use the same default slot cost/rules.
// ─────────────────────────────────────────────────────────────────────────────

using BotFramework.Host;
using BotFramework.Sdk;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Games.Dice;

[Command("/slot")]
[Command("/slots")]
[MessageDice("🎰")]
public sealed partial class DiceHandler(
    IDiceService service,
    ILocalizer localizer,
    ILogger<DiceHandler> logger) : IUpdateHandler
{
    private const string DiceEmoji = "🎰";
    private const string Usage = "🎰 <b>Слоты</b>\n"
        + "<code>/slot</code>, <code>/slots</code>, <code>slot</code> или <code>slots</code> — бот отправит 🎰 и применит обычную стоимость спина.\n"
        + "Можно просто отправить 🎰 — результат считается так же.";

    public async Task HandleAsync(UpdateContext ctx)
    {
        var diceMsg = ctx.MessageOrEdited;
        if (diceMsg?.Dice?.Emoji == DiceEmoji)
        {
            await HandleDiceAsync(ctx, diceMsg);
            return;
        }

        var msg = ctx.Update.Message;
        if (msg?.Text is null) return;

        var userId = msg.From?.Id ?? 0;
        if (userId == 0 || msg.From?.IsBot == true) return;

        var chatId = msg.Chat.Id;
        var reply = new ReplyParameters { MessageId = msg.MessageId };
        var displayName = msg.From?.Username ?? msg.From?.FirstName ?? $"User ID: {userId}";

        var parts = msg.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var action = parts.Length > 1 ? parts[1] : "";
        if (action.Equals("help", StringComparison.OrdinalIgnoreCase))
        {
            await ctx.Bot.SendMessage(chatId, Usage, parseMode: ParseMode.Html,
                replyParameters: reply, cancellationToken: ctx.Ct);
            return;
        }

        if (!string.IsNullOrWhiteSpace(action) && !action.Equals("spin", StringComparison.OrdinalIgnoreCase))
        {
            await ctx.Bot.SendMessage(chatId, Usage, parseMode: ParseMode.Html,
                replyParameters: reply, cancellationToken: ctx.Ct);
            return;
        }

        try
        {
            var diceSent = await ctx.Bot.SendDice(chatId, emoji: DiceEmoji, replyParameters: reply,
                cancellationToken: ctx.Ct);
            if (diceSent.Dice is { Value: > 0 })
            {
                await HandleDiceAsync(ctx, diceSent, userId, displayName, isForwarded: false);
            }
            else
            {
                BotMiniGameDiceOwner.Bind(chatId, diceSent.MessageId, userId, displayName);
            }
        }
        catch (Exception ex)
        {
            LogReplyFailed(userId, ex);
        }
    }

    private async Task HandleDiceAsync(UpdateContext ctx, Message msg)
    {
        if (!BotMiniGameDiceOwner.TryResolveDicePlayer(msg, out var userId, out var displayName))
            return;
        await HandleDiceAsync(ctx, msg, userId, displayName, msg.ForwardOrigin != null);
    }

    private async Task HandleDiceAsync(UpdateContext ctx, Message msg, long userId, string displayName, bool isForwarded)
    {
        if (msg.Dice is not { Value: > 0 }) return;

        var dice = msg.Dice!;
        var chatId = msg.Chat.Id;
        var reply = new ReplyParameters { MessageId = msg.MessageId };

        var result = await service.PlayAsync(
            userId, displayName, dice.Value, chatId, msg.MessageId,
            isForwarded,
            ctx.Ct);

        switch (result.Outcome)
        {
            case DiceOutcome.Forwarded:
                await ctx.Bot.SendMessage(chatId, Loc("err.forwarded"),
                    replyParameters: reply, cancellationToken: ctx.Ct);
                return;

            case DiceOutcome.NotEnoughCoins:
                await ctx.Bot.SendMessage(
                    chatId,
                    string.Format(Loc("err.not_enough_coins"), result.Loss),
                    replyParameters: reply, cancellationToken: ctx.Ct);
                return;

            case DiceOutcome.DailyRollLimitExceeded:
                await ctx.Bot.SendMessage(
                    chatId,
                    string.Format(Loc("err.daily_roll_limit"), result.DailyDiceUsed, result.DailyDiceLimit),
                    parseMode: ParseMode.Html, replyParameters: reply, cancellationToken: ctx.Ct);
                return;
        }

        var net = result.Prize - result.Loss;
        var isWin = net > 0;
        var lines = new[]
        {
            isWin
                ? string.Format(Loc("result.win"), result.Prize, result.Loss, net)
                : string.Format(Loc("result.lose"), result.Loss, result.Prize, -net),
            string.Format(Loc("result.balance"), result.NewBalance),
            result.Gas > 0 ? string.Format(Loc("result.gas"), result.Gas) : "",
            FormatRemainingAttempts(result.DailyDiceUsed, result.DailyDiceLimit),
        };
        var text = string.Join("\n", lines.Where(x => !string.IsNullOrWhiteSpace(x)));

        try
        {
            await Task.Delay(4000, ctx.Ct);
            await ctx.Bot.SendMessage(chatId, text,
                parseMode: ParseMode.Html, replyParameters: reply, cancellationToken: ctx.Ct);
        }
        catch (Exception ex)
        {
            LogReplyFailed(userId, ex);
        }
        finally
        {
            if (msg.From is { IsBot: true })
                BotMiniGameDiceOwner.MarkCompleted(chatId, msg.MessageId);
            else
                BotMiniGameDiceOwner.Unbind(chatId, msg.MessageId);
        }
    }

    private string Loc(string key) => localizer.Get("dice", key);

    private string FormatRemainingAttempts(int used, int limit) =>
        limit > 0
            ? string.Format(Loc("result.daily_roll_remaining"), Math.Max(0, limit - used), limit)
            : "";

    [LoggerMessage(EventId = 2001, Level = LogLevel.Error, Message = "dice.reply.failed user={UserId}")]
    partial void LogReplyFailed(long userId, Exception exception);
}

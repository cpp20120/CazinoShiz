// ─────────────────────────────────────────────────────────────────────────────
// DiceHandler — MessageDice("🎰") handler. Framework's UpdateRouter discovers
// this via the [MessageDice] attribute and dispatches every 🎰 from a user.
//
// Responsibility split against the monolith's DiceHandler:
//   • identical presentation: same Russian copy, HTML parse mode, reply to the
//     dice message. The text comes from the module-scoped ILocalizer so other
//     modules can reuse the framework's locale machinery without colliding.
//   • no freespin-code fan-out: that's Redeem's job and Redeem isn't a module
//     yet. Follow-up in #15.
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
        var msg = ctx.Update.Message!;
        var dice = msg.Dice!;
        var userId = msg.From?.Id ?? 0;
        if (userId == 0) return;

        var chatId = msg.Chat.Id;
        var reply = new ReplyParameters { MessageId = msg.MessageId };
        var displayName = msg.From?.Username ?? msg.From?.FirstName ?? $"User ID: {userId}";

        var result = await service.PlayAsync(
            userId, displayName, dice.Value, chatId,
            isForwarded: msg.ForwardOrigin != null,
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
        }

        var net = result.Prize - result.Loss;
        var isWin = net > 0;
        var text = string.Join("\n",
        [
            isWin
                ? string.Format(Loc("result.win"), result.Prize, result.Loss, net)
                : string.Format(Loc("result.lose"), result.Loss, result.Prize, -net),
            string.Format(Loc("result.balance"), result.NewBalance),
            result.Gas > 0 ? string.Format(Loc("result.gas"), result.Gas) : "",
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

    private string Loc(string key) => localizer.Get("dice", key);

    [LoggerMessage(EventId = 2001, Level = LogLevel.Error, Message = "dice.reply.failed user={UserId}")]
    partial void LogReplyFailed(long userId, Exception exception);
}

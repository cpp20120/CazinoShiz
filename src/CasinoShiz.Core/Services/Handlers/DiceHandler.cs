using CasinoShiz.Configuration;
using CasinoShiz.Helpers;
using CasinoShiz.Services.Dice;
using CasinoShiz.Services.Pipeline;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using static CasinoShiz.Helpers.RussianPlural;

namespace CasinoShiz.Services.Handlers;

[MessageDice(BotOptions.CasinoDice)]
public sealed partial class DiceHandler(
    DiceService service,
    ILogger<DiceHandler> logger) : IUpdateHandler
{
    public async Task HandleAsync(ITelegramBotClient bot, Update update, CancellationToken ct)
    {
        var msg = update.Message!;
        var dice = msg.Dice!;
        var userId = msg.From?.Id ?? 0;
        if (userId == 0) return;

        var chatId = msg.Chat.Id;
        var reply = new ReplyParameters { MessageId = msg.MessageId };
        var displayName = msg.From?.Username ?? msg.From?.FirstName ?? $"User ID: {userId}";

        var result = await service.PlayAsync(
            userId, displayName, dice.Value, chatId,
            isForwarded: msg.ForwardOrigin != null,
            isPrivateChat: msg.Chat.Type == ChatType.Private,
            ct);

        switch (result.Outcome)
        {
            case DiceOutcome.Forwarded:
                await bot.SendMessage(chatId, Locales.DoNotCheat(), replyParameters: reply, cancellationToken: ct);
                return;

            case DiceOutcome.AttemptsLimit:
                await bot.SendMessage(chatId, Locales.AttemptsLimit(result.TotalAttempts),
                    parseMode: ParseMode.Html, replyParameters: reply, cancellationToken: ct);
                return;

            case DiceOutcome.NotEnoughCoins:
                await bot.SendMessage(chatId, Locales.NotEnoughCoins(result.Loss),
                    replyParameters: reply, cancellationToken: ct);
                return;
        }

        var isWin = result.Prize - result.Loss > 0;
        var lines = new[]
        {
            isWin ? Locales.Win(result.Prize, result.Loss) : Locales.Lose(result.Loss, result.Prize),
            Locales.YourBalance(result.NewBalance),
            result.MoreRolls > 0
                ? $"(у Вас ещё {Plural(result.MoreRolls, ["попытка", "попытки", "попыток"], true)})"
                : "(у Вас больше не осталось попыток)",
            result.Gas > 0 ? Locales.GasReminder(result.Gas) : "",
            result.Tax > 0 ? Locales.BankTax(result.Tax, result.DaysWithoutRolls) : "",
        };

        try
        {
            await bot.SendMessage(chatId, string.Join("\n", lines),
                parseMode: ParseMode.Html, replyParameters: reply, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            LogReplyFailed(userId, ex);
            return;
        }

        if (result.FreespinCode is { } code)
        {
            try
            {
                var freespinMsg = await bot.SendMessage(chatId, Locales.FreespinQuote(code.ToString()),
                    parseMode: ParseMode.Html, replyParameters: reply, cancellationToken: ct);
                await service.AttachFreespinMessageAsync(code, freespinMsg.MessageId, ct);
            }
            catch (Exception ex)
            {
                LogFreespinSendFailed(userId, ex);
            }
        }
    }

    [LoggerMessage(EventId = 2001, Level = LogLevel.Error, Message = "dice.reply.failed user={UserId}")]
    partial void LogReplyFailed(long userId, Exception exception);

    [LoggerMessage(EventId = 2002, Level = LogLevel.Error, Message = "dice.freespin.send.failed user={UserId}")]
    partial void LogFreespinSendFailed(long userId, Exception exception);
}

using CasinoShiz.Configuration;
using CasinoShiz.Helpers;
using CasinoShiz.Services.Pipeline;
using CasinoShiz.Services.Redeem;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace CasinoShiz.Services.Handlers;

[Command("/redeem")]
[Command("/codegen")]
[CallbackFallback]
public sealed class RedeemHandler(
    RedeemService service,
    IOptions<BotOptions> options) : IUpdateHandler
{
    private readonly BotOptions _opts = options.Value;

    private static readonly Dictionary<long, CaptchaState> PendingCaptchas = new();

    public async Task HandleAsync(ITelegramBotClient bot, Update update, CancellationToken ct)
    {
        if (update.CallbackQuery != null)
        {
            await HandleCaptchaCallback(bot, update.CallbackQuery, ct);
            return;
        }

        var msg = update.Message!;
        var text = msg.Text!;

        if (text.StartsWith("/codegen"))
            await HandleCodeGen(bot, msg, ct);
        else if (text.StartsWith("/redeem"))
            await HandleRedeem(bot, msg, ct);
    }

    private async Task HandleCodeGen(ITelegramBotClient bot, Message msg, CancellationToken ct)
    {
        var userId = msg.From?.Id ?? 0;
        if (userId == 0 || !_opts.Admins.Contains(userId)) return;

        var code = await service.IssueAdminCodeAsync(userId, msg.Chat.Id, ct);
        await bot.SendMessage(msg.Chat.Id, code.ToString(), cancellationToken: ct);
    }

    private async Task HandleRedeem(ITelegramBotClient bot, Message msg, CancellationToken ct)
    {
        var userId = msg.From?.Id ?? 0;
        if (userId == 0) return;
        var chatId = msg.Chat.Id;
        var reply = new ReplyParameters { MessageId = msg.MessageId };

        if (msg.Chat.Type != ChatType.Private)
        {
            await bot.SendMessage(chatId,
                "Получить бесплатную крутку ты можешь отправив мне эту команду в личные сообщения 😄",
                replyParameters: reply, cancellationToken: ct);
            return;
        }

        var parts = msg.Text!.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var codeText = parts.Length > 1 ? parts[1] : "";

        var result = await service.BeginRedeemAsync(userId, chatId, codeText, ct);

        switch (result.Error)
        {
            case BeginRedeemError.InvalidCode:
                await bot.SendMessage(chatId, "Код недействителен", cancellationToken: ct);
                return;
            case BeginRedeemError.AlreadyRedeemed:
                await bot.SendMessage(chatId, "Сорри, этот код уже кто-то успел активировать 🤯", cancellationToken: ct);
                return;
            case BeginRedeemError.SelfRedeem:
                await bot.SendMessage(chatId, "Упс, а вот свой код обналичить нельзя 🥲", cancellationToken: ct);
                return;
            case BeginRedeemError.NoUser:
                await bot.SendMessage(chatId,
                    "Пока ты не сделаешь хотя бы одну крутку - ты не сможешь пользоваться чужими кодами 🥲",
                    cancellationToken: ct);
                return;
        }

        var captcha = result.Captcha!;
        var rows = (int)Math.Ceiling(captcha.Items.Length / 5.0);
        var splitAfter = Math.Max(1, (int)Math.Ceiling(captcha.Items.Length / (double)rows));

        var keyboardRows = new List<InlineKeyboardButton[]>();
        for (var i = 0; i < rows; i++)
        {
            keyboardRows.Add(captcha.Items
                .Skip(i * splitAfter).Take(splitAfter)
                .Select(item => InlineKeyboardButton.WithCallbackData(item.Text, item.Data.ToString()))
                .ToArray());
        }

        var captchaMsg = await bot.SendMessage(chatId,
            $"<b>{captcha.Pattern}</b>\nВыбери снизу самый подходящий смайлик",
            parseMode: ParseMode.Html,
            replyMarkup: new InlineKeyboardMarkup(keyboardRows),
            cancellationToken: ct);

        lock (PendingCaptchas)
        {
            PendingCaptchas[userId] = new CaptchaState
            {
                CodeGuid = result.CodeGuid,
                CodeText = codeText,
                TargetId = captcha.TargetId,
                Pattern = captcha.Pattern,
                MessageId = captchaMsg.MessageId,
            };
        }

        _ = Task.Run(async () =>
        {
            await Task.Delay(15_000, ct);
            lock (PendingCaptchas)
            {
                if (!PendingCaptchas.TryGetValue(userId, out var state) || state.MessageId != captchaMsg.MessageId)
                    return;
                PendingCaptchas.Remove(userId);
            }
            try
            {
                await bot.DeleteMessage(chatId, captchaMsg.MessageId, cancellationToken: ct);
                await bot.SendMessage(chatId, "Вы отвечали слишком долго, используйте /redeem снова", cancellationToken: ct);
            }
            catch (Exception) { /* message may already be gone */ }
        }, ct);
    }

    private async Task HandleCaptchaCallback(ITelegramBotClient bot, CallbackQuery cbq, CancellationToken ct)
    {
        var userId = cbq.From.Id;
        var chatId = cbq.Message?.Chat.Id ?? 0;
        if (chatId == 0) return;

        CaptchaState? state;
        lock (PendingCaptchas)
        {
            if (!PendingCaptchas.TryGetValue(userId, out state)) return;
            PendingCaptchas.Remove(userId);
        }

        try { await bot.DeleteMessage(chatId, state.MessageId, cancellationToken: ct); } catch (Exception) { /* captcha msg may be gone */ }

        var passed = cbq.Data == state.TargetId.ToString();
        service.ReportCaptcha(userId, chatId, state.CodeText, state.Pattern, passed);

        if (!passed)
        {
            await bot.SendMessage(chatId, "Увы, неверно. Сделайте /redeem снова", cancellationToken: ct);
            return;
        }

        var result = await service.CompleteRedeemAsync(userId, state.CodeGuid, state.CodeText, state.Pattern, chatId, ct);

        if (result.Error == CompleteRedeemError.AlreadyRedeemed)
        {
            await bot.SendMessage(chatId,
                "Ойй 😬, кажется пока кто-то решал капчу – кодик уже уплыл! 😜",
                cancellationToken: ct);
            return;
        }
        if (result.Error == CompleteRedeemError.NoUser) return;

        if (result.IssuedChatId is { } ich && result.IssuedMessageId is { } imid)
        {
            try
            {
                await bot.EditMessageText(ich, imid,
                    Locales.FreespinRedeemedQuote(), parseMode: ParseMode.Html, cancellationToken: ct);
            }
            catch (Exception) { /* original freespin post may be gone */ }
        }

        await bot.SendMessage(chatId,
            "Вот это скорость! У вас теперь есть еще одна крутка, и она будет действовать до полуночи",
            cancellationToken: ct);
    }

    private sealed class CaptchaState
    {
        public Guid CodeGuid { get; init; }
        public string CodeText { get; init; } = "";
        public int TargetId { get; init; }
        public string Pattern { get; init; } = "";
        public int MessageId { get; init; }
    }
}

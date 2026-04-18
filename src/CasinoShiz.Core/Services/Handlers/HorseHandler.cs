using CasinoShiz.Helpers;
using CasinoShiz.Services.Horse;
using CasinoShiz.Services.Pipeline;
using Microsoft.Extensions.Hosting;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace CasinoShiz.Services.Handlers;

[Command("/horse")]
[Command("/horserun")]
public sealed partial class HorseHandler(
    HorseService service,
    IHostApplicationLifetime lifetime,
    ILogger<HorseHandler> logger) : IUpdateHandler
{
    public async Task HandleAsync(ITelegramBotClient bot, Update update, CancellationToken ct)
    {
        var msg = update.Message!;
        var text = msg.Text!;
        var userId = msg.From?.Id ?? 0;
        if (userId == 0) return;

        if (text.StartsWith("/horserun"))
        {
            await HandleRunAsync(bot, msg, userId, ct);
            return;
        }

        var parts = StripFirst(text).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var action = parts.Length > 0 ? parts[0] : "";

        switch (action)
        {
            case "bet": await HandleBetAsync(bot, msg, userId, parts, ct); break;
            case "result": await HandleResultAsync(bot, msg, ct); break;
            case "info": await HandleInfoAsync(bot, msg, ct); break;
            case "help":
                await bot.SendMessage(msg.Chat.Id, Locales.HorsesHelp(), parseMode: ParseMode.Html,
                    replyParameters: new ReplyParameters { MessageId = msg.MessageId }, cancellationToken: ct);
                break;
            default:
                await bot.SendMessage(msg.Chat.Id, $"Неизвестное действие {action}\nМожно: bet, result, info",
                    replyParameters: new ReplyParameters { MessageId = msg.MessageId }, cancellationToken: ct);
                break;
        }
    }

    private async Task HandleBetAsync(ITelegramBotClient bot, Message msg, long userId, string[] parts, CancellationToken ct)
    {
        var chatId = msg.Chat.Id;
        var reply = new ReplyParameters { MessageId = msg.MessageId };

        if (parts.Length < 2 || !int.TryParse(parts[1], out int horseId))
        {
            await bot.SendMessage(chatId, $"Вы не указали номер лошади (1-{HorseService.HorseCount})",
                replyParameters: reply, cancellationToken: ct);
            return;
        }
        if (parts.Length < 3 || !int.TryParse(parts[2], out int amount))
        {
            await bot.SendMessage(chatId, "Вы не указали ставку",
                replyParameters: reply, cancellationToken: ct);
            return;
        }

        var displayName = msg.From?.Username ?? msg.From?.FirstName ?? $"User ID: {userId}";
        var r = await service.PlaceBetAsync(userId, displayName, horseId, amount, ct);

        string text = r.Error switch
        {
            HorseError.None => $"Вы поставили {r.Amount} на лошадь под номером {r.HorseId} на следующие скачки!",
            HorseError.InvalidHorseId => $"Указан неверный номер лошади (1-{HorseService.HorseCount})",
            HorseError.InvalidAmount => $"Указана неверная ставка (ваш баланс: {r.RemainingCoins})",
            _ => "Не удалось поставить ставку",
        };
        await bot.SendMessage(chatId, text, replyParameters: reply, cancellationToken: ct);
    }

    private async Task HandleRunAsync(ITelegramBotClient bot, Message msg, long userId, CancellationToken ct)
    {
        var chatId = msg.Chat.Id;

        var outcome = await service.RunRaceAsync(userId, ct);
        if (outcome.Error == HorseError.NotAdmin) return; // silent reject (matches prior behavior)
        if (outcome.Error == HorseError.NotEnoughBets)
        {
            await bot.SendMessage(chatId, $"Недостаточно ставок для забега (нужно {HorseService.MinBetsToRun})",
                replyParameters: new ReplyParameters { MessageId = msg.MessageId }, cancellationToken: ct);
            return;
        }

        await bot.SendMessage(chatId, "Активность запущена",
            replyParameters: new ReplyParameters { MessageId = msg.MessageId }, cancellationToken: ct);

        await using var gifStream = new MemoryStream(outcome.GifBytes);
        await bot.SendAnimation(chatId, InputFile.FromStream(gifStream, "horses.gif"), cancellationToken: ct);

        // Announce winners 20s after the GIF drops. The caller's `ct` is tied to the
        // per-update scope (webhook request or polling iteration) and can be cancelled
        // before the delay elapses — use the host lifetime token so it survives the
        // scope but still cancels cleanly on shutdown.
        var announceCt = lifetime.ApplicationStopping;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(20_000, announceCt);
                var text = outcome.Transactions.Count > 0
                    ? string.Join("\n", new[] { "<b>Поздравляем победителей!</b>\n" }
                        .Concat(outcome.Transactions.Select((tx, i) =>
                            $"<a href=\"tg://user?id={tx.UserId}\">Победитель {i + 1}</a>: <b>+{tx.Amount}</b>")))
                    : "<b>Сегодня никому не удалось победить :(</b>";
                await bot.SendMessage(chatId, text, parseMode: ParseMode.Html, cancellationToken: announceCt);
            }
            catch (OperationCanceledException) { /* app shutting down */ }
            catch (Exception ex) { LogHorseRunAnnounceFailed(ex); }
        }, announceCt);
    }

    private async Task HandleResultAsync(ITelegramBotClient bot, Message msg, CancellationToken ct)
    {
        var reply = new ReplyParameters { MessageId = msg.MessageId };
        var r = await service.GetTodayResultAsync(ct);

        if (r.Result != null)
        {
            await using var imageStream = new MemoryStream(r.Result.ImageData);
            await bot.SendPhoto(msg.Chat.Id, InputFile.FromStream(imageStream, "horses.jpg"),
                caption: $"Выиграла лошадь {r.Result.Winner + 1}",
                replyParameters: reply, cancellationToken: ct);
        }
        else
        {
            await bot.SendMessage(msg.Chat.Id, "Сегодня скачки еще не проводились",
                replyParameters: reply, cancellationToken: ct);
        }
    }

    private async Task HandleInfoAsync(ITelegramBotClient bot, Message msg, CancellationToken ct)
    {
        var info = await service.GetTodayInfoAsync(ct);
        var parts = new List<string> { Locales.StakesCreated(info.BetsCount) };
        if (info.BetsCount > 0) parts.Add(Locales.Koefs(info.Koefs));

        await bot.SendMessage(msg.Chat.Id, string.Join("\n\n", parts), parseMode: ParseMode.Html,
            replyParameters: new ReplyParameters { MessageId = msg.MessageId }, cancellationToken: ct);
    }

    private static string StripFirst(string str)
    {
        var parts = str.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 1 ? parts[1].Trim() : "";
    }

    [LoggerMessage(LogLevel.Debug, "horse.run.announce failed")]
    partial void LogHorseRunAnnounceFailed(Exception exception);
}

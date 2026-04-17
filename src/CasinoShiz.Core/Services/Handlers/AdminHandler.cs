using System.Text.Json;
using CasinoShiz.Configuration;
using CasinoShiz.Services.Admin;
using CasinoShiz.Services.Pipeline;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace CasinoShiz.Services.Handlers;

[Command("/run")]
[Command("/rename")]
public sealed class AdminHandler(
    AdminService service,
    IOptions<BotOptions> options) : IUpdateHandler
{
    private readonly BotOptions _opts = options.Value;

    public async Task HandleAsync(ITelegramBotClient bot, Update update, CancellationToken ct)
    {
        var msg = update.Message!;
        var text = msg.Text!;
        var userId = msg.From?.Id ?? 0;
        if (userId == 0) return;

        if (text.StartsWith("/rename"))
        {
            await HandleRename(bot, msg, userId, ct);
            return;
        }

        if (!_opts.Admins.Contains(userId))
        {
            await bot.SendMessage(msg.Chat.Id, "У вас нет прав, доступно только владельцу бота",
                replyParameters: new ReplyParameters { MessageId = msg.MessageId }, cancellationToken: ct);
            service.ReportNotAdmin(userId);
            return;
        }

        var parts = StripFirst(text).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var action = parts.Length > 0 ? parts[0] : "";
        var reply = new ReplyParameters { MessageId = msg.MessageId };

        switch (action)
        {
            case "usersync":
                await service.UserSyncAsync(userId, ct);
                await bot.SendMessage(msg.Chat.Id, "Пользователи синхронизированы",
                    replyParameters: reply, cancellationToken: ct);
                break;

            case "userinfo":
                if (msg.ReplyToMessage == null)
                {
                    await bot.SendMessage(msg.Chat.Id,
                        "Ответьте на сообщение пользователя с этой командой чтобы узнать его ID",
                        replyParameters: reply, cancellationToken: ct);
                    break;
                }
                var targetId = msg.ReplyToMessage.From?.Id.ToString() ?? "unknown";
                await bot.SendMessage(msg.Chat.Id, $"ID отправителя: <code>{targetId}</code>",
                    parseMode: ParseMode.Html, replyParameters: reply, cancellationToken: ct);
                service.ReportUserInfo(userId, targetId);
                break;

            case "pay":
                await HandlePay(bot, msg, userId, parts[1..], ct);
                break;

            case "getUser":
                await HandleGetUser(bot, msg, parts[1..], ct);
                break;

            default:
                await bot.SendMessage(msg.Chat.Id,
                    "available: \n- usersync \n- userinfo (replied) \n- pay <user_id> <amount>",
                    replyParameters: reply, cancellationToken: ct);
                break;
        }
    }

    private async Task HandlePay(ITelegramBotClient bot, Message msg, long userId, string[] args, CancellationToken ct)
    {
        var reply = new ReplyParameters { MessageId = msg.MessageId };
        if (args.Length < 2 || !long.TryParse(args[0], out var forUserId) || !int.TryParse(args[1], out var amount))
        {
            await bot.SendMessage(msg.Chat.Id, "Хинт: /run pay <user_id> <amount>",
                replyParameters: reply, cancellationToken: ct);
            return;
        }

        var r = await service.PayAsync(userId, forUserId, amount, ct);
        var diff = amount >= 0 ? $"+{amount}" : amount.ToString();
        await bot.SendMessage(msg.Chat.Id,
            $"Баланс юзера {r.DisplayName} ({forUserId}) \n{r.OldCoins}{diff} -> {r.NewCoins}",
            replyParameters: reply, cancellationToken: ct);
    }

    private async Task HandleGetUser(ITelegramBotClient bot, Message msg, string[] args, CancellationToken ct)
    {
        var reply = new ReplyParameters { MessageId = msg.MessageId };
        if (args.Length == 0 || !long.TryParse(args[0], out var forUserId))
        {
            await bot.SendMessage(msg.Chat.Id, "Хинт: /run getUser <user_id>",
                replyParameters: reply, cancellationToken: ct);
            return;
        }

        var r = await service.GetUserAsync(forUserId, ct);
        var json = r.User != null
            ? JsonSerializer.Serialize(r.User, new JsonSerializerOptions { WriteIndented = true })
            : "null";
        await bot.SendMessage(msg.Chat.Id, json, replyParameters: reply, cancellationToken: ct);
    }

    private async Task HandleRename(ITelegramBotClient bot, Message msg, long userId, CancellationToken ct)
    {
        if (!_opts.Admins.Contains(userId)) return;

        var parts = msg.Text!.Split(' ', 3);
        if (parts.Length < 3)
        {
            await bot.SendMessage(msg.Chat.Id, "... <old_name> <new_name/* to clear>", cancellationToken: ct);
            return;
        }

        var r = await service.RenameAsync(parts[1], parts[2], ct);
        var text = r.Op switch
        {
            RenameOp.Cleared => $"Renaming for {r.OldName} cleared",
            RenameOp.NoChange => $"Renaming for {r.OldName} cleared",
            _ => $"Renamed {r.OldName} to {r.NewName}",
        };
        await bot.SendMessage(msg.Chat.Id, text, cancellationToken: ct);
    }

    private static string StripFirst(string str)
    {
        var parts = str.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 1 ? parts[1].Trim() : "";
    }
}

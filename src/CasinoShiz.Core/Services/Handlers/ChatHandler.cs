using CasinoShiz.Data;
using CasinoShiz.Data.Entities;
using CasinoShiz.Services.Analytics;
using CasinoShiz.Services.Pipeline;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace CasinoShiz.Services.Handlers;

[Command("/regchat")]
[Command("/notification")]
public sealed partial class ChatHandler(
    AppDbContext db,
    ClickHouseReporter reporter,
    ILogger<ChatHandler> logger) : IUpdateHandler
{
    public async Task HandleAsync(ITelegramBotClient bot, Update update, CancellationToken ct)
    {
        var msg = update.Message!;
        var text = msg.Text!;

        if (text.StartsWith("/regchat"))
            await HandleRegChat(bot, msg, ct);
        else if (text.StartsWith("/notification"))
            await HandleNotification(bot, msg, ct);
    }

    private async Task HandleRegChat(ITelegramBotClient bot, Message msg, CancellationToken ct)
    {
        var userId = msg.From?.Id ?? 0;
        if (userId == 0) return;

        if (!await IsOwner(bot, msg.Chat.Id, userId, ct))
        {
            await bot.SendMessage(msg.Chat.Id, "У вас нет прав, доступно только владельцу",
                replyParameters: new ReplyParameters { MessageId = msg.MessageId }, cancellationToken: ct);
            return;
        }

        try
        {
            var existing = await db.Chats.FindAsync([msg.Chat.Id], ct);
            if (existing != null)
            {
                existing.Name = msg.Chat.Title ?? "";
                existing.Username = msg.Chat.Username;
                existing.NotificationsEnabled = true;
            }
            else
            {
                db.Chats.Add(new ChatRegistration
                {
                    ChatId = msg.Chat.Id,
                    Name = msg.Chat.Title ?? "",
                    Username = msg.Chat.Username,
                    NotificationsEnabled = true
                });
            }
            await db.SaveChangesAsync(ct);

            await bot.SendMessage(msg.Chat.Id, "Чат успешно зарегистрирован",
                replyParameters: new ReplyParameters { MessageId = msg.MessageId }, cancellationToken: ct);

            reporter.SendEvent(new EventData
            {
                EventType = "regchat",
                Payload = new { type = "success", chat_id = msg.Chat.Id, name = msg.Chat.Title, username = msg.Chat.Username }
            });
        }
        catch (Exception ex)
        {
            LogFailedToRegisterChat(ex);
            await bot.SendMessage(msg.Chat.Id, "Чат не зарегистрирован",
                replyParameters: new ReplyParameters { MessageId = msg.MessageId }, cancellationToken: ct);
            reporter.SendEvent(new EventData
            {
                EventType = "regchat",
                Payload = new { type = "error", chat_id = msg.Chat.Id, error = ex.Message }
            });
        }
    }

    private async Task HandleNotification(ITelegramBotClient bot, Message msg, CancellationToken ct)
    {
        var userId = msg.From?.Id ?? 0;
        if (userId == 0) return;

        if (!await IsOwner(bot, msg.Chat.Id, userId, ct))
        {
            await bot.SendMessage(msg.Chat.Id, "У вас нет прав, доступно только владельцу",
                replyParameters: new ReplyParameters { MessageId = msg.MessageId }, cancellationToken: ct);
            return;
        }

        var parts = msg.Text!.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var action = parts.Length > 1 ? parts[1] : "";

        var chat = await db.Chats.FindAsync([msg.Chat.Id], ct);

        switch (action)
        {
            case "get":
                if (chat == null)
                {
                    await bot.SendMessage(msg.Chat.Id, "Уведомления не настроены",
                        replyParameters: new ReplyParameters { MessageId = msg.MessageId }, cancellationToken: ct);
                }
                else
                {
                    await bot.SendMessage(msg.Chat.Id,
                        $"Уведомления {(chat.NotificationsEnabled ? "ВКЛ" : "ВЫКЛ")} (используйте 'allow/deny')",
                        replyParameters: new ReplyParameters { MessageId = msg.MessageId }, cancellationToken: ct);
                }
                break;
            case "allow":
                if (chat != null) { chat.NotificationsEnabled = true; await db.SaveChangesAsync(ct); }
                await bot.SendMessage(msg.Chat.Id, "Уведомления \"ВКЛ\" (используйте 'set allow/deny')",
                    replyParameters: new ReplyParameters { MessageId = msg.MessageId }, cancellationToken: ct);
                break;
            case "deny":
                if (chat != null) { chat.NotificationsEnabled = false; await db.SaveChangesAsync(ct); }
                await bot.SendMessage(msg.Chat.Id, "Уведомления \"ВЫКЛ\" (используйте 'set allow/deny')",
                    replyParameters: new ReplyParameters { MessageId = msg.MessageId }, cancellationToken: ct);
                break;
            default:
                await bot.SendMessage(msg.Chat.Id, "Неизвестное действие",
                    replyParameters: new ReplyParameters { MessageId = msg.MessageId }, cancellationToken: ct);
                break;
        }
    }

    private static async Task<bool> IsOwner(ITelegramBotClient bot, long chatId, long userId, CancellationToken ct)
    {
        try
        {
            var admins = await bot.GetChatAdministrators(chatId, cancellationToken: ct);
            return admins.Any(a => a.User.Id == userId && a.Status == ChatMemberStatus.Creator);
        }
        catch
        {
            return false;
        }
    }

    [LoggerMessage(LogLevel.Error, "Failed to register chat")]
    partial void LogFailedToRegisterChat(Exception exception);
}

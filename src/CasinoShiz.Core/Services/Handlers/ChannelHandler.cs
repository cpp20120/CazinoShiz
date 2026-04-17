using CasinoShiz.Configuration;
using CasinoShiz.Data;
using CasinoShiz.Services.Analytics;
using CasinoShiz.Services.Pipeline;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace CasinoShiz.Services.Handlers;

[ChannelPost]
public sealed partial class ChannelHandler(
    AppDbContext db,
    IOptions<BotOptions> options,
    ClickHouseReporter reporter,
    ILogger<ChannelHandler> logger) : IUpdateHandler
{
    private readonly BotOptions _opts = options.Value;

    public async Task HandleAsync(ITelegramBotClient bot, Update update, CancellationToken ct)
    {
        var post = update.ChannelPost!;
        var chatUsername = post.Chat.Username;

        if (chatUsername != _opts.TrustedChannel.TrimStart('@'))
        {
            try
            {
                await bot.LeaveChat(post.Chat.Id, cancellationToken: ct);
                reporter.SendEvent(new EventData
                {
                    EventType = "leave_channel",
                    Payload = new { chat_id = post.Chat.Id, status = true }
                });
            }
            catch (Exception ex)
            {
                LogFailedToLeaveChannelChatid(post.Chat.Id, ex);
                reporter.SendEvent(new EventData
                {
                    EventType = "leave_channel",
                    Payload = new { chat_id = post.Chat.Id, status = false }
                });
            }
            return;
        }

        var subscribedChats = await db.Chats
            .Where(c => c.NotificationsEnabled)
            .Select(c => c.ChatId)
            .ToListAsync(ct);

        foreach (var targetChatId in subscribedChats)
        {
            try
            {
                await bot.ForwardMessage(targetChatId, post.Chat.Id, post.MessageId, cancellationToken: ct);
                reporter.SendEvent(new EventData
                {
                    EventType = "forward_channel_post",
                    Payload = new { post = post.MessageId, chat_id = post.Chat.Id, status = true, error = "" }
                });
            }
            catch (Exception ex)
            {
                reporter.SendEvent(new EventData
                {
                    EventType = "forward_channel_post",
                    Payload = new { post = post.MessageId, chat_id = post.Chat.Id, status = false, error = ex.Message }
                });
            }
        }
    }

    [LoggerMessage(LogLevel.Error, "Failed to leave channel {ChatId}")]
    partial void LogFailedToLeaveChannelChatid(long chatId, Exception exception);
}

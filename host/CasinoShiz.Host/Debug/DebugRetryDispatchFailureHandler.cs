using BotFramework.Host;
using BotFramework.Host.Composition;
using BotFramework.Sdk;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace CasinoShiz.Host.Debug;

[Command("/__debug_retry_dispatch_failure")]
public sealed class DebugRetryDispatchFailureHandler(
    IEventDispatchRetryService retryService,
    IConfiguration configuration,
    IOptions<BotFrameworkOptions> botOptions) : IUpdateHandler
{
    public async Task HandleAsync(UpdateContext ctx)
    {
        var msg = ctx.Update.Message;
        if (msg?.Text is null) return;
        if (!DebugAccess.IsAllowed(msg, configuration, botOptions.Value)) return;

        var parts = msg.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2 || !long.TryParse(parts[1], out var id))
        {
            await ctx.Bot.SendMessage(
                msg.Chat.Id,
                "usage: /__debug_retry_dispatch_failure <id>",
                replyParameters: new ReplyParameters { MessageId = msg.MessageId },
                cancellationToken: ctx.Ct);
            return;
        }

        var result = await retryService.RetryAsync(id, ctx.Ct);
        var text = result.Success
            ? $"retry ok #{id}"
            : $"retry failed #{id}: {result.Message}";

        await ctx.Bot.SendMessage(
            msg.Chat.Id,
            text,
            replyParameters: new ReplyParameters { MessageId = msg.MessageId },
            cancellationToken: ctx.Ct);
    }
}

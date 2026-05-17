using BotFramework.Host;
using BotFramework.Host.Composition;
using BotFramework.Sdk;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace CasinoShiz.Host.Debug;

[Command("/__debug_dispatch_failures")]
public sealed class DebugDispatchFailuresHandler(
    IEventDispatchFailureStore failures,
    IConfiguration configuration,
    IOptions<BotFrameworkOptions> botOptions) : IUpdateHandler
{
    public async Task HandleAsync(UpdateContext ctx)
    {
        var msg = ctx.Update.Message;
        if (msg?.Text is null) return;
        if (!DebugAccess.IsAllowed(msg, configuration, botOptions.Value)) return;

        var rows = await failures.ListUnresolvedAsync(10, ctx.Ct);
        var lines = new List<string> { $"unresolved dispatch failures: {rows.Count}" };
        foreach (var row in rows)
            lines.Add($"#{row.Id} {row.EventType} {row.StreamId}@{row.StreamVersion} retries={row.RetryCount}");

        await ctx.Bot.SendMessage(
            msg.Chat.Id,
            string.Join('\n', lines),
            replyParameters: new ReplyParameters { MessageId = msg.MessageId },
            cancellationToken: ctx.Ct);
    }
}

internal static class DebugAccess
{
    public static bool IsAllowed(Message msg, IConfiguration configuration, BotFrameworkOptions options)
    {
        if (!configuration.GetValue("Debug:Enabled", defaultValue: true)) return false;
        if (!configuration.GetValue("Debug:RequireAdmin", defaultValue: true)) return true;
        var userId = msg.From?.Id ?? 0;
        foreach (var id in options.Admins)
            if (id == userId) return true;
        foreach (var id in options.ReadOnlyAdmins)
            if (id == userId) return true;
        return false;
    }
}

using System.Net;
using BotFramework.Sdk;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace CasinoShiz.Host.Debug;

[Command("/__debug_es")]
public sealed class DebugEsSmokeHandler(
    IRepository<DebugEsSmokeAggregate> repository,
    IConfiguration configuration,
    IOptions<BotFrameworkOptions> botOptions) : IUpdateHandler
{
    public async Task HandleAsync(UpdateContext ctx)
    {
        var msg = ctx.Update.Message;
        if (msg?.Text is null) return;

        var enabled = configuration.GetValue("Debug:Enabled", defaultValue: true);
        if (!enabled)
            return;

        if (configuration.GetValue("Debug:RequireAdmin", defaultValue: true))
        {
            var userId = msg.From?.Id ?? 0;
            if (!IsAnyAdmin(botOptions.Value, userId))
            {
                await ctx.Bot.SendMessage(
                    msg.Chat.Id,
                    "🚫 <b>Debug ES</b>: not allowed.",
                    parseMode: ParseMode.Html,
                    replyParameters: new ReplyParameters { MessageId = msg.MessageId },
                    cancellationToken: ctx.Ct);
                return;
            }
        }

        var streamId = GetStreamId(msg);
        var aggregate = await repository.FindAsync(streamId, ctx.Ct)
            ?? new DebugEsSmokeAggregate(streamId);

        var beforeVersion = aggregate.Version;
        var beforeCount = aggregate.Count;

        aggregate.Increment(msg.From?.Id ?? 0, msg.Chat.Id);
        await repository.SaveAsync(aggregate, ctx.Ct);

        var text = string.Join('\n',
            "✅ <b>Debug ES smoke event persisted</b>",
            $"stream: <code>{Enc(streamId)}</code>",
            $"version: <code>{beforeVersion} → {aggregate.Version}</code>",
            $"count: <code>{beforeCount} → {aggregate.Count}</code>",
            "tables: <code>module_events</code> and <code>event_log</code> should both increment");

        await ctx.Bot.SendMessage(
            msg.Chat.Id,
            text,
            parseMode: ParseMode.Html,
            replyParameters: new ReplyParameters { MessageId = msg.MessageId },
            cancellationToken: ctx.Ct);
    }

    private static string GetStreamId(Message msg)
    {
        var parts = msg.Text?.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts is { Length: > 1 })
            return $"debug-es:{parts[1]}";

        return $"debug-es:{msg.Chat.Id}:{msg.From?.Id ?? 0}";
    }

    private static bool IsAnyAdmin(BotFrameworkOptions o, long userId)
    {
        foreach (var id in o.Admins)
            if (id == userId) return true;
        foreach (var id in o.ReadOnlyAdmins)
            if (id == userId) return true;
        return false;
    }

    private static string Enc<T>(T value) => WebUtility.HtmlEncode(value?.ToString() ?? "");
}

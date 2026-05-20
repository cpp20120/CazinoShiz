using System.Net;
using BotFramework.Sdk;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Games.Meta;

[Command("/risk")]
public sealed class RiskHandler(IRiskService risks) : IUpdateHandler
{
    public async Task HandleAsync(UpdateContext ctx)
    {
        var msg = ctx.Update.Message;
        if (msg?.Text is null || !msg.Text.StartsWith("/risk", StringComparison.OrdinalIgnoreCase)) return;

        var parts = msg.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 1 || string.Equals(parts[1], "list", StringComparison.OrdinalIgnoreCase))
        {
            await HandleListAsync(ctx, msg);
            return;
        }

        if (parts.Length >= 3 && long.TryParse(parts[2], out var flagId) &&
            (string.Equals(parts[1], "resolve", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(parts[1], "ignore", StringComparison.OrdinalIgnoreCase)))
        {
            var result = await risks.UpdateStatusAsync(flagId, parts[1], ctx.Ct);
            await SendHtmlAsync(ctx, msg, result.Updated ? $"✅ {Html(result.Message)}" : $"❌ {Html(result.Message)}");
            return;
        }

        await SendHtmlAsync(ctx, msg, string.Join("\n", [
            "🛡 <b>Risk control</b>",
            "<code>/risk</code> или <code>/risk list</code>",
            "<code>/risk resolve &lt;id&gt;</code>",
            "<code>/risk ignore &lt;id&gt;</code>",
        ]));
    }

    private async Task HandleListAsync(UpdateContext ctx, Message msg)
    {
        var rows = await risks.GetOpenAsync(msg.Chat.Id, 20, ctx.Ct);
        if (rows.Count == 0)
        {
            await SendHtmlAsync(ctx, msg, "🛡 Открытых risk flags нет.");
            return;
        }

        var lines = new List<string> { "🛡 <b>Open risk flags</b>" };
        foreach (var row in rows)
        {
            lines.Add($"#<code>{row.Id}</code> <b>{Html(row.Severity)}</b> · <code>{Html(row.Kind)}</code> · <b>{Html(row.DisplayName)}</b>");
            lines.Add($"   {Html(row.Reason)} · <code>{row.CreatedAt:yyyy-MM-dd HH:mm 'UTC'}</code>");
        }

        await SendHtmlAsync(ctx, msg, string.Join("\n", lines));
    }

    private Task SendHtmlAsync(UpdateContext ctx, Message msg, string text) =>
        ctx.Bot.SendMessage(msg.Chat.Id, text,
            parseMode: ParseMode.Html,
            replyParameters: new ReplyParameters { MessageId = msg.MessageId },
            cancellationToken: ctx.Ct);

    private static string Html(string value) => WebUtility.HtmlEncode(value);
}

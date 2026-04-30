// ─────────────────────────────────────────────────────────────────────────────
// AnalyticsHandler — admin-only `/analytics` command.
//
// Gated to private chats and to user IDs in Bot:Admins (BotFrameworkOptions),
// same gating pattern as the leaderboard /topall and the framework's
// DebugHandler. Output is rendered as a single Telegram HTML message,
// chunked when over the 4096-char limit.
//
// Sub-commands:
//   /analytics                 — default report (top 5, 14-day timeline)
//   /analytics top <N>         — pin top-N to N (clamped to [1, 50])
//   /analytics days <N>        — pin timeline length to N (clamped to [1, 90])
//   /analytics top <N> days <N>
// ─────────────────────────────────────────────────────────────────────────────

using System.Net;
using System.Text;
using BotFramework.Host;
using BotFramework.Host.Composition;
using BotFramework.Host.Services;
using BotFramework.Sdk;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Games.Admin;

[Command("/analytics")]
public sealed class AnalyticsHandler(
    IAnalyticsQueryService analytics,
    ILocalizer localizer,
    IOptions<BotFrameworkOptions> botOptions,
    ILogger<AnalyticsHandler> logger) : IUpdateHandler
{
    private readonly BotFrameworkOptions _bot = botOptions.Value;

    public async Task HandleAsync(UpdateContext ctx)
    {
        var msg = ctx.Update.Message;
        if (msg?.Text is null) return;

        var userId = msg.From?.Id ?? 0;
        var reply = new ReplyParameters { MessageId = msg.MessageId };

        if (msg.Chat.Type != ChatType.Private)
        {
            await ctx.Bot.SendMessage(msg.Chat.Id, Loc("private_only"),
                parseMode: ParseMode.Html, replyParameters: reply, cancellationToken: ctx.Ct);
            return;
        }

        if (userId == 0 || !_bot.Admins.Contains(userId))
        {
            await ctx.Bot.SendMessage(msg.Chat.Id, Loc("not_admin"),
                parseMode: ParseMode.Html, replyParameters: reply, cancellationToken: ctx.Ct);
            return;
        }

        var (topN, timelineDays) = ParseArgs(msg.Text);

        var status = await analytics.GetStatusAsync(ctx.Ct);
        if (!status.Configured)
        {
            var text = string.Format(Loc("disabled"), HtmlEnc(status.Error ?? ""));
            await ctx.Bot.SendMessage(msg.Chat.Id, text,
                parseMode: ParseMode.Html, replyParameters: reply, cancellationToken: ctx.Ct);
            return;
        }
        if (!status.Reachable)
        {
            var text = string.Format(Loc("unreachable"), HtmlEnc(status.Error ?? ""));
            await ctx.Bot.SendMessage(msg.Chat.Id, text,
                parseMode: ParseMode.Html, replyParameters: reply, cancellationToken: ctx.Ct);
            return;
        }

        AnalyticsReport report;
        try
        {
            report = await analytics.GetReportAsync(topN, timelineDays, ctx.Ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "analytics command failed user={UserId}", userId);
            var text = string.Format(Loc("query_failed"), HtmlEnc(ex.Message));
            await ctx.Bot.SendMessage(msg.Chat.Id, text,
                parseMode: ParseMode.Html, replyParameters: reply, cancellationToken: ctx.Ct);
            return;
        }

        var rendered = Render(report);
        await SendChunkedAsync(ctx, msg.Chat.Id, rendered, reply);
    }

    private static (int topN, int timelineDays) ParseArgs(string text)
    {
        const int defaultTop = 5;
        const int defaultDays = 14;

        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var top = defaultTop;
        var days = defaultDays;
        for (var i = 1; i + 1 < parts.Length; i++)
        {
            if (parts[i].Equals("top", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(parts[i + 1], out var t)) top = t;
            else if (parts[i].Equals("days", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(parts[i + 1], out var d)) days = d;
        }
        return (top, days);
    }

    private string Render(AnalyticsReport r)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Format(Loc("header"),
            HtmlEnc(r.Project),
            HtmlEnc(r.TableName),
            r.TotalRowsAllTime,
            r.GeneratedAtUtc.ToString("yyyy-MM-dd HH:mm:ss 'UTC'")));
        sb.AppendLine();

        foreach (var w in r.Windows)
        {
            sb.AppendLine(string.Format(Loc("window.header"), HtmlEnc(w.Label)));
            sb.AppendLine(string.Format(Loc("window.totals"), w.TotalEvents, w.DistinctUsers));

            if (w.TopEventTypes.Count > 0)
            {
                sb.AppendLine(Loc("window.top_events"));
                foreach (var e in w.TopEventTypes)
                    sb.AppendLine($"  • <code>{HtmlEnc(e.Name)}</code> — {e.Count}");
            }

            if (w.TopModules.Count > 0)
            {
                sb.AppendLine(Loc("window.top_modules"));
                foreach (var m in w.TopModules)
                    sb.AppendLine($"  • <code>{HtmlEnc(m.Name)}</code> — {m.Count}");
            }

            if (w.TopUsers.Count > 0)
            {
                sb.AppendLine(Loc("window.top_users"));
                foreach (var u in w.TopUsers)
                    sb.AppendLine($"  • <code>{u.UserId}</code> — {u.Count}");
            }

            sb.AppendLine();
        }

        if (r.Timeline.Count > 0)
        {
            sb.AppendLine(string.Format(Loc("timeline.header"), r.Timeline.Count));
            var max = r.Timeline.Max(b => b.Count);
            foreach (var b in r.Timeline)
            {
                var bar = max == 0 ? "" : new string('▌', (int)Math.Min(20, b.Count * 20 / Math.Max(1, max)));
                sb.AppendLine($"<code>{b.Day:yyyy-MM-dd}</code>  <code>{b.Count,6}</code>  {bar}");
            }
        }

        return sb.ToString().TrimEnd();
    }

    private async Task SendChunkedAsync(UpdateContext ctx, long chatId, string text, ReplyParameters reply)
    {
        const int maxLen = 3800;
        if (text.Length <= maxLen)
        {
            await ctx.Bot.SendMessage(chatId, text,
                parseMode: ParseMode.Html,
                replyParameters: reply,
                cancellationToken: ctx.Ct);
            return;
        }

        var chunks = new List<string>();
        var pending = new StringBuilder();
        foreach (var line in text.Split('\n'))
        {
            if (pending.Length + line.Length + 1 > maxLen && pending.Length > 0)
            {
                chunks.Add(pending.ToString().TrimEnd());
                pending.Clear();
            }
            pending.AppendLine(line);
        }
        if (pending.Length > 0) chunks.Add(pending.ToString().TrimEnd());

        for (var i = 0; i < chunks.Count; i++)
        {
            await ctx.Bot.SendMessage(chatId, chunks[i],
                parseMode: ParseMode.Html,
                replyParameters: i == 0 ? reply : null,
                cancellationToken: ctx.Ct);
        }
    }

    private static string HtmlEnc(string s) => WebUtility.HtmlEncode(s ?? "");

    private string Loc(string key) => localizer.Get("admin", $"analytics.{key}");
}

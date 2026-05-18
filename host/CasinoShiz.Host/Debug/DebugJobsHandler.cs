using System.Net;
using System.Text;
using BotFramework.Host.Composition;
using BotFramework.Sdk;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace CasinoShiz.Host.Debug;

[Command("/__debug_jobs")]
public sealed class DebugJobsHandler(
    IBackgroundJobStatusService jobs,
    IConfiguration configuration,
    IOptions<BotFrameworkOptions> botOptions) : IUpdateHandler
{
    public async Task HandleAsync(UpdateContext ctx)
    {
        var msg = ctx.Update.Message;
        if (msg?.Text is null) return;

        var enabled = configuration.GetValue("Debug:Enabled", defaultValue: true);
        if (!enabled) return;

        if (configuration.GetValue("Debug:RequireAdmin", defaultValue: true))
        {
            var userId = msg.From?.Id ?? 0;
            if (!IsAnyAdmin(botOptions.Value, userId))
            {
                await ctx.Bot.SendMessage(
                    msg.Chat.Id,
                    "🚫 <b>Debug jobs</b>: not allowed.",
                    parseMode: ParseMode.Html,
                    replyParameters: new ReplyParameters { MessageId = msg.MessageId },
                    cancellationToken: ctx.Ct);
                return;
            }
        }

        var snapshots = jobs.Snapshot();
        var sb = new StringBuilder();
        sb.AppendLine("<b>Background jobs</b>");
        if (snapshots.Count == 0)
        {
            sb.AppendLine("No module background jobs registered.");
        }
        else
        {
            foreach (var job in snapshots)
            {
                sb.AppendLine();
                sb.AppendLine($"<b>{Enc(job.Name)}</b>");
                sb.AppendLine($"state: <code>{Enc(job.State)}</code>");
                sb.AppendLine($"heartbeat: <code>{Fmt(job.LastHeartbeatAt)}</code>");
                sb.AppendLine($"started: <code>{Fmt(job.LastStartedAt)}</code>");
                sb.AppendLine($"completed: <code>{Fmt(job.LastCompletedAt)}</code>");
                sb.AppendLine($"failed: <code>{Fmt(job.LastFailedAt)}</code>");
                sb.AppendLine($"crashes: <code>{job.CrashCount}</code>");
                if (job.RestartBackoffMs.HasValue)
                    sb.AppendLine($"restart backoff: <code>{job.RestartBackoffMs.Value}ms</code>");
                if (!string.IsNullOrWhiteSpace(job.LastError))
                    sb.AppendLine($"error: <code>{Enc(job.LastError)}</code>");
            }
        }

        await ctx.Bot.SendMessage(
            msg.Chat.Id,
            sb.ToString().TrimEnd(),
            parseMode: ParseMode.Html,
            replyParameters: new ReplyParameters { MessageId = msg.MessageId },
            cancellationToken: ctx.Ct);
    }

    private static bool IsAnyAdmin(BotFrameworkOptions o, long userId)
    {
        foreach (var id in o.Admins)
            if (id == userId) return true;
        foreach (var id in o.ReadOnlyAdmins)
            if (id == userId) return true;
        return false;
    }

    private static string Fmt(DateTimeOffset? value) =>
        value?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "—";

    private static string Enc<T>(T value) => WebUtility.HtmlEncode(value?.ToString() ?? "");
}

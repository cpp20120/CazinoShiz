using System.Diagnostics;
using System.Net;
using System.Text;
using BotFramework.Host.Composition;
using BotFramework.Sdk;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace CasinoShiz.Host.Debug;

[Command("/__debug")]
public sealed class DebugHandler(
    BotProcessClock clock,
    IConfiguration configuration,
    IHostEnvironment hostEnvironment,
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
                    "🚫 <b>Debug</b>: not allowed.",
                    parseMode: ParseMode.Html,
                    replyParameters: new ReplyParameters { MessageId = msg.MessageId },
                    cancellationToken: ctx.Ct);
                return;
            }
        }

        var text = await BuildDebugTextAsync(msg, ctx.Ct);
        await ctx.Bot.SendMessage(
            msg.Chat.Id,
            text,
            parseMode: ParseMode.Html,
            replyParameters: new ReplyParameters { MessageId = msg.MessageId },
            cancellationToken: ctx.Ct);
    }

    private async Task<string> BuildDebugTextAsync(Message msg, CancellationToken ct)
    {
        var chatId = msg.Chat.Id;
        var chatType = msg.Chat.Type.ToString();
        var userId = msg.From?.Id ?? 0;

        var hasPg = !string.IsNullOrWhiteSpace(configuration.GetConnectionString("Postgres"));
        var httpProxy = Environment.GetEnvironmentVariable("HTTP_PROXY");
        var httpsProxy = Environment.GetEnvironmentVariable("HTTPS_PROXY");
        var proxyMode = string.IsNullOrEmpty(httpProxy) && string.IsNullOrEmpty(httpsProxy)
            ? "none"
            : $"http={httpProxy ?? "—"} https={httpsProxy ?? "—"}";

        var uptimeSec = (int)(DateTime.UtcNow - clock.StartedAtUtc).TotalSeconds;

        using var process = Process.GetCurrentProcess();
        var startCpu = process.TotalProcessorTime;
        var startWall = DateTime.UtcNow;
        await Task.Delay(100, ct);
        process.Refresh();
        var cpuMs = (process.TotalProcessorTime - startCpu).TotalMilliseconds;
        var wallMs = (DateTime.UtcNow - startWall).TotalMilliseconds;
        var cpuPercent = wallMs > 0
            ? cpuMs / (Environment.ProcessorCount * wallMs) * 100.0
            : 0;

        var workingSetMb = process.WorkingSet64 / 1024.0 / 1024.0;
        var gcBytes = GC.GetTotalMemory(false);

        var sb = new StringBuilder();
        sb.AppendLine($"chat id: <code>{Enc(chatId)}</code>");
        sb.AppendLine($"chat type: <code>{Enc(chatType)}</code>");
        sb.AppendLine($"user id: <code>{Enc(userId)}</code>");
        sb.AppendLine($"ASPNETCORE_ENVIRONMENT: <code>{Enc(hostEnvironment.EnvironmentName)}</code>");
        sb.AppendLine($"postgres conn: <code>{(hasPg ? "yes" : "no")}</code>");
        sb.AppendLine($"proxy: <code>{Enc(proxyMode)}</code>");
        sb.AppendLine($"uptime: <code>{uptimeSec}s</code>");
        sb.AppendLine($"cpu (sample 100ms): <code>{cpuPercent:F1}%</code>");
        sb.AppendLine($"rss: <code>{workingSetMb:F1} MB</code>");
        sb.AppendLine($"GC heap: <code>{gcBytes / 1024.0 / 1024.0:F1} MB</code>");
        return sb.ToString().TrimEnd();
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

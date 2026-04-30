// ─────────────────────────────────────────────────────────────────────────────
// ChatsHandler — admin-only `/chats` command. Lists every chat the bot has
// observed (via known_chats) with type, title, ID, freshness, and engagement
// stats joined from the users table.
//
// Same gating as /topall and /analytics: private chat + Bot:Admins only.
// Output is chunked to fit Telegram's 4096-char message limit.
//
// Sub-commands:
//   /chats                 — every chat, default limit 50, newest first
//   /chats group           — only groups + supergroups
//   /chats private         — only private chats
//   /chats channel         — only channels
//   /chats full            — no row limit (paginated by Telegram chunking)
//   /chats <type> full     — combine
// ─────────────────────────────────────────────────────────────────────────────

using System.Net;
using System.Text;
using BotFramework.Host;
using BotFramework.Host.Composition;
using BotFramework.Sdk;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Games.Admin;

[Command("/chats")]
public sealed class ChatsHandler(
    IChatsStore store,
    ILocalizer localizer,
    IOptions<BotFrameworkOptions> botOptions) : IUpdateHandler
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
            await ctx.Bot.SendMessage(msg.Chat.Id, Loc("chats.private_only"),
                parseMode: ParseMode.Html, replyParameters: reply, cancellationToken: ctx.Ct);
            return;
        }

        if (userId == 0 || !_bot.Admins.Contains(userId))
        {
            await ctx.Bot.SendMessage(msg.Chat.Id, Loc("chats.not_admin"),
                parseMode: ParseMode.Html, replyParameters: reply, cancellationToken: ctx.Ct);
            return;
        }

        var (typeFilter, full) = ParseArgs(msg.Text);
        var limit = full ? 0 : 50;

        var rows = await store.ListChatsAsync(typeFilter, limit, ctx.Ct);
        var totalMatching = await store.CountChatsAsync(typeFilter, ctx.Ct);

        if (rows.Count == 0)
        {
            await ctx.Bot.SendMessage(msg.Chat.Id, Loc("chats.empty"),
                replyParameters: reply, cancellationToken: ctx.Ct);
            return;
        }

        var rendered = Render(rows, typeFilter, totalMatching, full);
        await SendChunkedAsync(ctx, msg.Chat.Id, rendered, reply);
    }

    private static (string? typeFilter, bool full) ParseArgs(string text)
    {
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string? typeFilter = null;
        var full = false;

        for (var i = 1; i < parts.Length; i++)
        {
            var token = parts[i].ToLowerInvariant();
            switch (token)
            {
                case "private":
                case "group":
                case "channel":
                    typeFilter = token;
                    break;
                case "supergroup":
                    typeFilter = "group"; // map to the "group" bucket which covers both
                    break;
                case "full":
                case "all":
                    full = true;
                    break;
            }
        }
        return (typeFilter, full);
    }

    private string Render(IReadOnlyList<KnownChatRow> rows, string? typeFilter, int totalMatching, bool full)
    {
        var nowUtc = DateTime.UtcNow;
        var sb = new StringBuilder();

        var headerKey = typeFilter switch
        {
            "private" => "chats.header.private",
            "group" => "chats.header.group",
            "channel" => "chats.header.channel",
            _ => "chats.header.all",
        };
        sb.AppendLine(string.Format(Loc(headerKey), totalMatching));
        sb.AppendLine();

        var grouped = rows
            .GroupBy(r => r.ChatType)
            .OrderBy(g => TypeOrder(g.Key))
            .ThenBy(g => g.Key);

        foreach (var group in grouped)
        {
            sb.AppendLine(string.Format(Loc("chats.section_header"),
                HtmlEnc(group.Key), group.Count()));
            foreach (var row in group)
            {
                sb.AppendLine(FormatRow(row, nowUtc));
            }
            sb.AppendLine();
        }

        if (!full && rows.Count < totalMatching)
            sb.AppendLine(string.Format(Loc("chats.truncated"), rows.Count, totalMatching));

        return sb.ToString().TrimEnd();
    }

    private string FormatRow(KnownChatRow row, DateTime nowUtc)
    {
        var title = !string.IsNullOrWhiteSpace(row.Title)
            ? row.Title!
            : row.ChatType.Equals("private", StringComparison.OrdinalIgnoreCase)
                ? string.Format(Loc("chats.private_label"), row.ChatId)
                : string.Format(Loc("chats.unknown_label"), row.ChatId);

        var titleHtml = HtmlEnc(title);
        var usernameHtml = string.IsNullOrWhiteSpace(row.Username)
            ? ""
            : $" · @{HtmlEnc(row.Username!)}";
        var lastSeen = HumanizeAge(nowUtc - row.LastSeenAt.ToUniversalTime());
        var firstSeen = row.FirstSeenAt.ToUniversalTime().ToString("yyyy-MM-dd");

        return $"• <b>{titleHtml}</b>{usernameHtml}\n" +
               $"  <code>{row.ChatId}</code> · users <b>{row.UserCount}</b> · coins <b>{row.TotalCoins}</b>\n" +
               $"  seen {lastSeen} · since {firstSeen}";
    }

    private string HumanizeAge(TimeSpan delta)
    {
        if (delta.TotalSeconds < 0) delta = TimeSpan.Zero;
        if (delta.TotalMinutes < 1) return Loc("chats.ago.now");
        if (delta.TotalMinutes < 60) return string.Format(Loc("chats.ago.minutes"), (int)delta.TotalMinutes);
        if (delta.TotalHours < 24) return string.Format(Loc("chats.ago.hours"), (int)delta.TotalHours);
        if (delta.TotalDays < 30) return string.Format(Loc("chats.ago.days"), (int)delta.TotalDays);
        return string.Format(Loc("chats.ago.months"), (int)(delta.TotalDays / 30));
    }

    private static int TypeOrder(string chatType) => chatType.ToLowerInvariant() switch
    {
        "supergroup" => 0,
        "group" => 1,
        "channel" => 2,
        "private" => 3,
        _ => 4,
    };

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

    private static string HtmlEnc(string? s) => WebUtility.HtmlEncode(s ?? "");

    private string Loc(string key) => localizer.Get("admin", key);
}

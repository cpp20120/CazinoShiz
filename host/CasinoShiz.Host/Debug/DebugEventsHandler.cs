using System.Text;
using BotFramework.Host;
using BotFramework.Host.Composition;
using BotFramework.Sdk;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace CasinoShiz.Host.Debug;

[Command("/__debug_events")]
public sealed class DebugEventsHandler(
    INpgsqlConnectionFactory connections,
    IConfiguration configuration,
    IOptions<BotFrameworkOptions> botOptions) : IUpdateHandler
{
    private const int EventLimit = 20;
    private const int TelegramTextLimit = 4_000;

    public async Task HandleAsync(UpdateContext ctx)
    {
        var msg = ctx.Update.Message;
        if (msg?.Text is null) return;
        if (!configuration.GetValue("Debug:Enabled", defaultValue: true)) return;

        if (configuration.GetValue("Debug:RequireAdmin", defaultValue: true)
            && !IsAnyAdmin(botOptions.Value, msg.From?.Id ?? 0))
        {
            await ReplyAsync(ctx, msg, "🚫 <b>Debug events</b>: not allowed.");
            return;
        }

        var query = ParseQuery(msg.Text);
        if (string.IsNullOrWhiteSpace(query))
        {
            await ReplyAsync(ctx, msg, "usage: <code>/__debug_events &lt;@username|user_id&gt;</code>");
            return;
        }

        await using var conn = await connections.OpenAsync(ctx.Ct);
        var resolved = await ResolveUserAsync(conn, query, ctx.Ct);
        if (resolved.Count == 0)
        {
            await ReplyAsync(ctx, msg, $"User <code>{Enc(query)}</code> not found.");
            return;
        }

        if (resolved.Count > 1)
        {
            var candidates = string.Join("\n", resolved.Select(x =>
                $"• <code>{x.UserId}</code> — {Enc(x.DisplayName)}"));
            await ReplyAsync(
                ctx,
                msg,
                $"Several users match <code>{Enc(query)}</code>:\n{candidates}\n\nUse numeric user ID.");
            return;
        }

        var user = resolved[0];
        var events = await conn.QueryAsync<DebugEventRow>(new CommandDefinition(
            """
            SELECT id AS Id,
                   event_type AS EventType,
                   payload::text AS PayloadJson,
                   occurred_at AS OccurredAt
            FROM event_log
            WHERE EXISTS (
                SELECT 1
                FROM jsonb_each_text(payload) field
                WHERE (
                    field.key ILIKE '%userid'
                    OR field.key IN ('IssuedBy', 'RedeemedBy', 'CreatedBy')
                )
                  AND field.value = @userIdText
            )
            ORDER BY occurred_at DESC, id DESC
            LIMIT @limit
            """,
            new
            {
                userIdText = user.UserId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                limit = EventLimit,
            },
            cancellationToken: ctx.Ct));

        var rows = events.ToList();
        var text = FormatEvents(user, rows);
        await ReplyAsync(ctx, msg, text);
    }

    private static async Task<IReadOnlyList<DebugUserRow>> ResolveUserAsync(
        System.Data.Common.DbConnection conn,
        string query,
        CancellationToken ct)
    {
        var normalized = query.Trim().TrimStart('@');
        if (long.TryParse(normalized, out var userId))
        {
            var row = await conn.QuerySingleOrDefaultAsync<DebugUserRow>(new CommandDefinition(
                """
                SELECT @userId AS UserId,
                       COALESCE(
                           (
                               SELECT display_name
                               FROM users
                               WHERE telegram_user_id = @userId
                               ORDER BY updated_at DESC
                               LIMIT 1
                           ),
                           @fallback
                       ) AS DisplayName
                """,
                new { userId, fallback = $"User ID: {userId}" },
                cancellationToken: ct));
            return row is null ? [] : [row];
        }

        var rows = await conn.QueryAsync<DebugUserRow>(new CommandDefinition(
            """
            SELECT telegram_user_id AS UserId,
                   (array_agg(display_name ORDER BY updated_at DESC))[1] AS DisplayName
            FROM users
            WHERE lower(trim(leading '@' FROM display_name)) = lower(@username)
            GROUP BY telegram_user_id
            ORDER BY max(updated_at) DESC
            LIMIT 6
            """,
            new { username = normalized },
            cancellationToken: ct));
        return rows.ToList();
    }

    private static string FormatEvents(DebugUserRow user, IReadOnlyList<DebugEventRow> events)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<b>Last events</b>");
        sb.AppendLine($"user: <b>{Enc(user.DisplayName)}</b> · <code>{user.UserId}</code>");
        sb.AppendLine();

        if (events.Count == 0)
        {
            sb.Append("No events found.");
            return sb.ToString();
        }

        foreach (var row in events)
        {
            var payload = Compact(row.PayloadJson, 90);
            var line =
                $"<code>{row.OccurredAt.ToLocalTime():dd.MM HH:mm:ss}</code> " +
                $"<b>{Enc(row.EventType)}</b>\n<code>{Enc(payload)}</code>\n";

            if (sb.Length + line.Length > TelegramTextLimit)
            {
                sb.AppendLine("…output truncated.");
                break;
            }

            sb.Append(line);
        }

        return sb.ToString().TrimEnd();
    }

    private static string? ParseQuery(string text)
    {
        var parts = text.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 2 ? parts[1] : null;
    }

    private static string Compact(string value, int maxLength)
    {
        var compact = string.Join(' ', value.Split(
            ['\r', '\n', '\t'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return compact.Length <= maxLength ? compact : compact[..(maxLength - 1)] + "…";
    }

    private static bool IsAnyAdmin(BotFrameworkOptions options, long userId) =>
        options.Admins.Contains(userId) || options.ReadOnlyAdmins.Contains(userId);

    private static Task ReplyAsync(UpdateContext ctx, Message msg, string text) =>
        ctx.Bot.SendMessage(
            msg.Chat.Id,
            text,
            parseMode: ParseMode.Html,
            replyParameters: new ReplyParameters { MessageId = msg.MessageId },
            cancellationToken: ctx.Ct);

    private static string Enc<T>(T value) => (value?.ToString() ?? "")
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal);

    private sealed record DebugUserRow(long UserId, string DisplayName);

    private sealed record DebugEventRow(
        long Id,
        string EventType,
        string PayloadJson,
        DateTimeOffset OccurredAt);
}

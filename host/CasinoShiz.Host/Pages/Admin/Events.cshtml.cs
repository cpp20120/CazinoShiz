using BotFramework.Host;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CasinoShiz.Host.Pages.Admin;

public sealed class EventsModel(INpgsqlConnectionFactory connections) : PageModel
{
    public IReadOnlyList<EventRow> Rows { get; private set; } = [];
    public IReadOnlyList<string> Modules { get; private set; } = [];
    public IReadOnlyList<ChatOptionRow> Chats { get; private set; } = [];

    [BindProperty(SupportsGet = true)]
    public string? Module { get; set; }

    /// <summary>Filter to events whose JSON payload has matching <c>ChatId</c> (not all event types have it).</summary>
    [BindProperty(SupportsGet = true)]
    public long? ChatId { get; set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);

        var modules = await conn.QueryAsync<string>(new CommandDefinition(
            "SELECT DISTINCT split_part(event_type, '.', 1) FROM event_log ORDER BY 1",
            cancellationToken: ct));
        Modules = modules.ToList();

        var chats = await conn.QueryAsync<ChatOptionRow>(new CommandDefinition(
            """
            SELECT k.chat_id AS ChatId,
                   (k.chat_type || ' · ' || coalesce(k.title, k.username, k.chat_id::text)) AS Label
            FROM known_chats k
            ORDER BY k.last_seen_at DESC
            LIMIT 200
            """, cancellationToken: ct));
        Chats = chats.ToList();

        const string sql = """
            SELECT id AS Id, event_type AS EventType, payload::text AS PayloadJson,
                   occurred_at AS OccurredAt
            FROM event_log
            WHERE (@module = '' OR split_part(event_type, '.', 1) = @module)
              AND (
                  @chatId IS NULL
                  OR (payload ? 'ChatId' AND (payload->>'ChatId')::bigint = @chatId)
              )
            ORDER BY id DESC
            LIMIT 200
            """;
        var rows = await conn.QueryAsync<EventRow>(new CommandDefinition(
            sql, new { module = Module ?? "", chatId = ChatId }, cancellationToken: ct));
        Rows = rows.ToList();
    }
}

public sealed record ChatOptionRow(long ChatId, string Label);

public sealed record EventRow(long Id, string EventType, string PayloadJson, DateTimeOffset OccurredAt);

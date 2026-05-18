using BotFramework.Host;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CasinoShiz.Host.Pages.Admin;

public sealed class EventsModel(INpgsqlConnectionFactory connections) : PageModel
{
    public IReadOnlyList<AdminEventRow> Rows { get; private set; } = [];
    public IReadOnlyList<string> Modules { get; private set; } = [];
    public IReadOnlyList<ChatOptionRow> Chats { get; private set; } = [];
    public IReadOnlyList<EventTypeCountRow> EventTypeCounts { get; private set; } = [];

    [BindProperty(SupportsGet = true)]
    public string? Module { get; set; }

    /// <summary>Filter to events whose JSON payload has matching <c>ChatId</c> (not all event types have it).</summary>
    [BindProperty(SupportsGet = true)]
    public long? ChatId { get; set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);

        var modules = await conn.QueryAsync<string>(new CommandDefinition("""
            SELECT module FROM (
                SELECT DISTINCT split_part(event_type, '.', 1) AS module FROM event_log
                UNION
                SELECT DISTINCT split_part(event_type, '.', 1) AS module FROM module_events
            ) m
            ORDER BY module
            """, cancellationToken: ct));
        Modules = modules.ToList();

        var chats = await conn.QueryAsync<ChatOptionRow>(new CommandDefinition("""
            SELECT k.chat_id AS ChatId,
                   (k.chat_type || ' · ' || coalesce(k.title, k.username, k.chat_id::text)) AS Label
            FROM known_chats k
            ORDER BY k.last_seen_at DESC
            LIMIT 200
            """, cancellationToken: ct));
        Chats = chats.ToList();

        const string countsSql = """
            SELECT source AS Source, event_type AS EventType, count(*)::int AS Count
            FROM (
                SELECT 'domain' AS source, event_type, payload
                FROM event_log
                WHERE (@module = '' OR split_part(event_type, '.', 1) = @module)
                  AND (
                      @chatId IS NULL
                      OR (payload ? 'ChatId' AND (payload->>'ChatId')::bigint = @chatId)
                  )
                UNION ALL
                SELECT 'es' AS source, event_type, payload
                FROM module_events
                WHERE (@module = '' OR split_part(event_type, '.', 1) = @module)
                  AND (
                      @chatId IS NULL
                      OR (payload ? 'ChatId' AND (payload->>'ChatId')::bigint = @chatId)
                  )
            ) e
            GROUP BY source, event_type
            ORDER BY split_part(event_type, '.', 1), event_type, source
            """;
        var counts = await conn.QueryAsync<EventTypeCountRow>(new CommandDefinition(
            countsSql, new { module = Module ?? "", chatId = ChatId }, cancellationToken: ct));
        EventTypeCounts = counts.ToList();

        const string sql = """
            SELECT source AS Source,
                   id AS Id,
                   stream_id AS StreamId,
                   version AS Version,
                   event_type AS EventType,
                   payload::text AS PayloadJson,
                   occurred_at AS OccurredAt
            FROM (
                SELECT 'domain' AS source,
                       id,
                       NULL::text AS stream_id,
                       NULL::bigint AS version,
                       event_type,
                       payload,
                       occurred_at
                FROM event_log
                WHERE (@module = '' OR split_part(event_type, '.', 1) = @module)
                  AND (
                      @chatId IS NULL
                      OR (payload ? 'ChatId' AND (payload->>'ChatId')::bigint = @chatId)
                  )

                UNION ALL

                SELECT 'es' AS source,
                       id,
                       stream_id,
                       version,
                       event_type,
                       payload,
                       occurred_at
                FROM module_events
                WHERE (@module = '' OR split_part(event_type, '.', 1) = @module)
                  AND (
                      @chatId IS NULL
                      OR (payload ? 'ChatId' AND (payload->>'ChatId')::bigint = @chatId)
                  )
            ) e
            ORDER BY occurred_at DESC, id DESC
            LIMIT 300
            """;
        var rows = await conn.QueryAsync<AdminEventRow>(new CommandDefinition(
            sql, new { module = Module ?? "", chatId = ChatId }, cancellationToken: ct));
        Rows = rows.ToList();
    }
}

public sealed record ChatOptionRow(long ChatId, string Label);

public sealed record EventTypeCountRow(string Source, string EventType, int Count);

public sealed record AdminEventRow(
    string Source,
    long Id,
    string? StreamId,
    long? Version,
    string EventType,
    string PayloadJson,
    DateTimeOffset OccurredAt);
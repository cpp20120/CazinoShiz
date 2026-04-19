using BotFramework.Host;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CasinoShiz.Host.Pages.Admin;

public sealed class EventsModel(INpgsqlConnectionFactory connections) : PageModel
{
    public IReadOnlyList<EventRow> Rows { get; private set; } = [];
    public IReadOnlyList<string> Modules { get; private set; } = [];

    [BindProperty(SupportsGet = true)]
    public string? Module { get; set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);

        var modules = await conn.QueryAsync<string>(new CommandDefinition(
            "SELECT DISTINCT split_part(event_type, '.', 1) FROM event_log ORDER BY 1",
            cancellationToken: ct));
        Modules = modules.ToList();

        const string sql = """
            SELECT id AS Id, event_type AS EventType, payload::text AS PayloadJson,
                   occurred_at AS OccurredAt
            FROM event_log
            WHERE (@module = '' OR split_part(event_type, '.', 1) = @module)
            ORDER BY id DESC
            LIMIT 200
            """;
        var rows = await conn.QueryAsync<EventRow>(new CommandDefinition(
            sql, new { module = Module ?? "" }, cancellationToken: ct));
        Rows = rows.ToList();
    }
}

public sealed record EventRow(long Id, string EventType, string PayloadJson, DateTimeOffset OccurredAt);

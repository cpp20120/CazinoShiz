using BotFramework.Host;
using BotFramework.Host.Services;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CasinoShiz.Host.Pages.Admin;

public sealed class GroupsModel(INpgsqlConnectionFactory connections) : PageModel
{
    public IReadOnlyList<GroupRow> Groups { get; private set; } = [];

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    /// <summary>When true, include private (DM) chats. Default: only group/supergroup/channel.</summary>
    [BindProperty(SupportsGet = true)]
    public bool Dms { get; set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        _ = HttpContext.Session.GetAdminSession();
        var q = (Q ?? "").Trim();
        const string sql = """
            SELECT
                k.chat_id     AS ChatId,
                k.chat_type   AS ChatType,
                k.title       AS Title,
                k.username    AS Username,
                k.first_seen_at AS FirstSeenAt,
                k.last_seen_at  AS LastSeenAt
            FROM known_chats k
            WHERE (@dms = true OR k.chat_type IN ('group', 'supergroup', 'channel'))
              AND (
                  @q = ''
                  OR k.chat_id::text = @q
                  OR coalesce(k.title, '') ILIKE '%' || @q || '%'
                  OR coalesce(k.username, '') ILIKE '%' || @q || '%'
              )
            ORDER BY k.last_seen_at DESC
            LIMIT 500
            """;
        await using var conn = await connections.OpenAsync(ct);
        var rows = await conn.QueryAsync<GroupRow>(new CommandDefinition(
            sql, new { q, dms = Dms }, cancellationToken: ct));
        Groups = rows.ToList();
    }
}

public sealed record GroupRow(
    long ChatId,
    string ChatType,
    string? Title,
    string? Username,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt);

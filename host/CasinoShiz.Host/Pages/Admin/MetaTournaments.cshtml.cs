using BotFramework.Host;
using BotFramework.Host.Services;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CasinoShiz.Host.Pages.Admin;

public sealed class MetaTournamentsModel(INpgsqlConnectionFactory connections) : PageModel
{
    public IReadOnlyList<MetaTournamentRow> Rows { get; private set; } = [];

    [BindProperty(SupportsGet = true)] public string Status { get; set; } = "all";
    [BindProperty(SupportsGet = true)] public string? ChatId { get; set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        var actor = HttpContext.Session.GetAdminSession();
        if (actor is null) return RedirectToPage("/Admin/Login");

        var status = NormalizeStatus(Status);
        Status = status;
        long? chatId = long.TryParse(ChatId, out var cid) ? cid : null;

        const string sql = """
            SELECT t.id AS Id,
                   t.season_id AS SeasonId,
                   s.name AS SeasonName,
                   t.chat_id AS ChatId,
                   t.game_key AS GameKey,
                   t.type AS Type,
                   t.status AS Status,
                   t.entry_fee AS EntryFee,
                   t.max_players AS MaxPlayers,
                   t.created_by AS CreatedBy,
                   t.created_at AS CreatedAt,
                   count(p.user_id)::int AS PlayerCount,
                   (count(p.user_id) * t.entry_fee)::bigint AS PrizePool,
                   count(m.id)::int AS MatchCount,
                   count(m.id) FILTER (WHERE m.status = 'ready')::int AS ReadyMatchCount,
                   count(m.id) FILTER (WHERE m.status IN ('finished', 'byed'))::int AS DoneMatchCount
            FROM meta_tournaments t
            JOIN meta_seasons s ON s.id = t.season_id
            LEFT JOIN meta_tournament_players p ON p.tournament_id = t.id AND p.status IN ('joined', 'winner', 'eliminated')
            LEFT JOIN meta_tournament_matches m ON m.tournament_id = t.id
            WHERE (@status = 'all' OR t.status = @status)
              AND (@chatId IS NULL OR t.chat_id = @chatId)
            GROUP BY t.id, t.season_id, s.name, t.chat_id, t.game_key, t.type, t.status, t.entry_fee, t.max_players, t.created_by, t.created_at
            ORDER BY t.created_at DESC
            LIMIT 200
            """;

        await using var conn = await connections.OpenAsync(ct);
        Rows = (await conn.QueryAsync<MetaTournamentRow>(new CommandDefinition(sql, new { status, chatId }, cancellationToken: ct))).ToList();
        return Page();
    }

    private static string NormalizeStatus(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "open" => "open",
        "started" => "started",
        "cancelled" => "cancelled",
        "finished" => "finished",
        _ => "all",
    };
}

public sealed record MetaTournamentRow(
    long Id,
    long SeasonId,
    string SeasonName,
    long ChatId,
    string GameKey,
    string Type,
    string Status,
    int EntryFee,
    int MaxPlayers,
    long CreatedBy,
    DateTimeOffset CreatedAt,
    int PlayerCount,
    long PrizePool,
    int MatchCount,
    int ReadyMatchCount,
    int DoneMatchCount);

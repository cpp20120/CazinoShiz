using BotFramework.Host;
using BotFramework.Host.Services;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CasinoShiz.Host.Pages.Admin;

public sealed class MetaTournamentModel(INpgsqlConnectionFactory connections) : PageModel
{
    public MetaTournamentDetail? Tournament { get; private set; }
    public IReadOnlyList<MetaTournamentPlayerRow> Players { get; private set; } = [];
    public IReadOnlyList<MetaTournamentMatchRow> Matches { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(long id, CancellationToken ct)
    {
        var actor = HttpContext.Session.GetAdminSession();
        if (actor is null) return RedirectToPage("/Admin/Login");

        const string tournamentSql = """
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
                   (count(p.user_id) * t.entry_fee)::bigint AS PrizePool
            FROM meta_tournaments t
            JOIN meta_seasons s ON s.id = t.season_id
            LEFT JOIN meta_tournament_players p ON p.tournament_id = t.id AND p.status IN ('joined', 'winner', 'eliminated')
            WHERE t.id = @id
            GROUP BY t.id, t.season_id, s.name, t.chat_id, t.game_key, t.type, t.status, t.entry_fee, t.max_players, t.created_by, t.created_at
            LIMIT 1
            """;

        const string playersSql = """
            SELECT tournament_id AS TournamentId,
                   user_id AS UserId,
                   display_name AS DisplayName,
                   status AS Status,
                   joined_at AS JoinedAt
            FROM meta_tournament_players
            WHERE tournament_id = @id
            ORDER BY joined_at ASC
            """;

        const string matchesSql = """
            SELECT id AS Id,
                   tournament_id AS TournamentId,
                   round AS Round,
                   match_index AS MatchIndex,
                   status AS Status,
                   player1_user_id AS Player1UserId,
                   player1_display_name AS Player1DisplayName,
                   player2_user_id AS Player2UserId,
                   player2_display_name AS Player2DisplayName,
                   victor_user_id AS VictorUserId,
                   created_at AS CreatedAt,
                   updated_at AS UpdatedAt
            FROM meta_tournament_matches
            WHERE tournament_id = @id
            ORDER BY round ASC, match_index ASC
            """;

        await using var conn = await connections.OpenAsync(ct);
        Tournament = await conn.QuerySingleOrDefaultAsync<MetaTournamentDetail>(new CommandDefinition(tournamentSql, new { id }, cancellationToken: ct));
        if (Tournament is null) return NotFound();

        Players = (await conn.QueryAsync<MetaTournamentPlayerRow>(new CommandDefinition(playersSql, new { id }, cancellationToken: ct))).ToList();
        Matches = (await conn.QueryAsync<MetaTournamentMatchRow>(new CommandDefinition(matchesSql, new { id }, cancellationToken: ct))).ToList();
        return Page();
    }
}

public sealed record MetaTournamentDetail(
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
    long PrizePool);

public sealed record MetaTournamentPlayerRow(
    long TournamentId,
    long UserId,
    string DisplayName,
    string Status,
    DateTimeOffset JoinedAt);

public sealed record MetaTournamentMatchRow(
    long Id,
    long TournamentId,
    int Round,
    int MatchIndex,
    string Status,
    long? Player1UserId,
    string? Player1DisplayName,
    long? Player2UserId,
    string? Player2DisplayName,
    long? VictorUserId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

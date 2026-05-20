using BotFramework.Host;
using BotFramework.Host.Services;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CasinoShiz.Host.Pages.Admin;

public sealed class MetaModel(INpgsqlConnectionFactory connections) : PageModel
{
    public string? ActiveSeasonName { get; private set; }
    public long? ActiveSeasonId { get; private set; }
    public int TotalSeasons { get; private set; }
    public int SeasonPlayers { get; private set; }
    public int OpenAlerts { get; private set; }
    public int Clans { get; private set; }
    public int OpenTournaments { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        var actor = HttpContext.Session.GetAdminSession();
        if (actor is null) return RedirectToPage("/Admin/Login");

        await using var conn = await connections.OpenAsync(ct);

        var active = await conn.QuerySingleOrDefaultAsync<ActiveSeasonRow>(new CommandDefinition(
            "SELECT id AS Id, name AS Name FROM meta_seasons WHERE status = 'active' ORDER BY starts_at DESC LIMIT 1",
            cancellationToken: ct));
        ActiveSeasonId = active?.Id;
        ActiveSeasonName = active?.Name;

        TotalSeasons = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT count(*)::int FROM meta_seasons", cancellationToken: ct));
        SeasonPlayers = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT count(*)::int FROM meta_season_players", cancellationToken: ct));
        OpenAlerts = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT count(*)::int FROM meta_risk_flags WHERE status = 'open'", cancellationToken: ct));
        Clans = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT count(*)::int FROM meta_clans", cancellationToken: ct));
        OpenTournaments = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT count(*)::int FROM meta_tournaments WHERE status = 'open'", cancellationToken: ct));

        return Page();
    }

    private sealed record ActiveSeasonRow(long Id, string Name);
}

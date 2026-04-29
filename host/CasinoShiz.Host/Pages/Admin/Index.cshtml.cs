using BotFramework.Host;
using BotFramework.Sdk;
using Dapper;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CasinoShiz.Host.Pages.Admin;

public sealed class IndexModel(
    INpgsqlConnectionFactory connections,
    IEnumerable<IModule> modules) : PageModel
{
    public int PeopleCount { get; private set; }
    public int WalletRowCount { get; private set; }
    public long TotalCoins { get; private set; }
    public int EventCount { get; private set; }
    public int PendingBets { get; private set; }
    public IReadOnlyList<IModule> Modules { get; private set; } = modules.ToList();
    public IReadOnlyList<ModuleCount> EventsByModule { get; private set; } = [];
    public IReadOnlyList<MiniGameStickerTracking> StickerGames { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);

        PeopleCount = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT count(DISTINCT telegram_user_id)::int FROM users", cancellationToken: ct));
        WalletRowCount = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT count(*)::int FROM users", cancellationToken: ct));
        TotalCoins = await conn.ExecuteScalarAsync<long?>(new CommandDefinition(
            "SELECT coalesce(sum(coins), 0)::bigint FROM users", cancellationToken: ct)) ?? 0;
        EventCount = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT count(*)::int FROM event_log", cancellationToken: ct));

        var pendingSql = string.Join(" + ", new[]
        {
            "(SELECT count(*) FROM darts_rounds)",
            "(SELECT count(*) FROM dicecube_bets)",
            "(SELECT count(*) FROM basketball_bets)",
            "(SELECT count(*) FROM bowling_bets)",
            "(SELECT count(*) FROM blackjack_hands)",
            "(SELECT count(*) FROM horse_bets)",
        });
        PendingBets = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            $"SELECT ({pendingSql})::int", cancellationToken: ct));

        var rows = await conn.QueryAsync<(string module, int cnt)>(new CommandDefinition("""
            SELECT split_part(event_type, '.', 1) AS module, count(*)::int AS cnt
            FROM event_log
            GROUP BY 1
            ORDER BY 2 DESC
            """, cancellationToken: ct));
        EventsByModule = rows.Select(r => new ModuleCount(r.module, r.cnt)).ToList();

        var stickerRows = await conn.QueryAsync<MiniGameStickerTracking>(new CommandDefinition("""
            WITH games(game_id, label, play_event) AS (
                VALUES
                    ('dice', 'slots', 'dice.roll_completed'),
                    ('dicecube', 'dicecube', 'dicecube.roll_completed'),
                    ('darts', 'darts', 'darts.throw_completed'),
                    ('football', 'football', 'football.throw_completed'),
                    ('basketball', 'basketball', 'basketball.throw_completed'),
                    ('bowling', 'bowling', 'bowling.roll_completed')
            )
            SELECT
                g.game_id AS GameId,
                g.label AS Label,
                count(e.id)::int AS Plays,
                count(e.id) FILTER (WHERE e.occurred_at >= current_date)::int AS PlaysToday,
                max(e.occurred_at) AS LastPlayedAt
            FROM games g
            LEFT JOIN event_log e ON e.event_type = g.play_event
            GROUP BY g.game_id, g.label
            ORDER BY count(e.id) DESC, g.game_id
            """, cancellationToken: ct));
        StickerGames = stickerRows.ToList();
    }
}

public sealed record ModuleCount(string Module, int Count);

public sealed class MiniGameStickerTracking
{
    public string GameId { get; init; } = "";
    public string Label { get; init; } = "";
    public int Plays { get; init; }
    public int PlaysToday { get; init; }
    public DateTimeOffset? LastPlayedAt { get; init; }
}
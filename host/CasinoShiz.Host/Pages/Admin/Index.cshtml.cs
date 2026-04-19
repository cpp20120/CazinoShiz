using BotFramework.Host;
using BotFramework.Sdk;
using Dapper;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CasinoShiz.Host.Pages.Admin;

public sealed class IndexModel(
    INpgsqlConnectionFactory connections,
    IEnumerable<IModule> modules) : PageModel
{
    public int UserCount { get; private set; }
    public long TotalCoins { get; private set; }
    public int EventCount { get; private set; }
    public int PendingBets { get; private set; }
    public IReadOnlyList<IModule> Modules { get; private set; } = modules.ToList();
    public IReadOnlyList<ModuleCount> EventsByModule { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);

        UserCount = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT count(*)::int FROM users", cancellationToken: ct));
        TotalCoins = await conn.ExecuteScalarAsync<long?>(new CommandDefinition(
            "SELECT coalesce(sum(coins), 0)::bigint FROM users", cancellationToken: ct)) ?? 0;
        EventCount = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT count(*)::int FROM event_log", cancellationToken: ct));

        var pendingSql = string.Join(" + ", new[]
        {
            "(SELECT count(*) FROM darts_bets)",
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
    }
}

public sealed record ModuleCount(string Module, int Count);

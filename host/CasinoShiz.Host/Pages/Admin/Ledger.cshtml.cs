using BotFramework.Host;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CasinoShiz.Host.Pages.Admin;

public sealed class LedgerModel(INpgsqlConnectionFactory connections) : PageModel
{
    public IReadOnlyList<LedgerRow> Rows { get; private set; } = [];

    [BindProperty(SupportsGet = true)]
    public string? U { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? S { get; set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        long? userId = long.TryParse(U, out var uid) ? uid : null;
        long? scopeId = long.TryParse(S, out var sid) ? sid : null;

        const string sql = """
            SELECT l.id AS Id,
                   l.telegram_user_id AS TelegramUserId,
                   l.balance_scope_id AS BalanceScopeId,
                   l.delta AS Delta,
                   l.balance_after AS BalanceAfter,
                   l.reason AS Reason,
                   l.created_at AS CreatedAt
            FROM economics_ledger l
            WHERE (@userId IS NULL OR l.telegram_user_id = @userId)
              AND (@scopeId IS NULL OR l.balance_scope_id = @scopeId)
            ORDER BY l.id DESC
            LIMIT 500
            """;

        await using var conn = await connections.OpenAsync(ct);
        var rows = await conn.QueryAsync<LedgerRow>(new CommandDefinition(
            sql, new { userId, scopeId }, cancellationToken: ct));
        Rows = rows.ToList();
    }
}

public sealed record LedgerRow(
    long Id,
    long TelegramUserId,
    long BalanceScopeId,
    int Delta,
    int BalanceAfter,
    string Reason,
    DateTimeOffset CreatedAt);

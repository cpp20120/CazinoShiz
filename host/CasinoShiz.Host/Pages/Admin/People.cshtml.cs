using BotFramework.Host;
using BotFramework.Host.Services;
using BotFramework.Sdk;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CasinoShiz.Host.Pages.Admin;

/// <summary>
/// One row per Telegram user — a directory view. Wallets and balance edits live on /admin/users.
/// </summary>
public sealed class PeopleModel(INpgsqlConnectionFactory connections) : PageModel
{
    public IReadOnlyList<PersonRow> People { get; private set; } = [];
    public AdminSession? Actor { get; private set; }

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        Actor = HttpContext.Session.GetAdminSession();

        const string sql = """
            WITH w AS (
                SELECT
                    u.telegram_user_id,
                    (array_agg(u.display_name ORDER BY u.updated_at DESC))[1] AS display_name,
                    count(*)::int                                              AS wallet_count,
                    max(u.updated_at)                                          AS last_active
                FROM users u
                WHERE (@q = '' OR u.telegram_user_id::text = @q
                        OR u.display_name ILIKE '%' || @q || '%')
                GROUP BY u.telegram_user_id
            )
            SELECT
                w.telegram_user_id AS UserId,
                w.display_name       AS DisplayName,
                w.wallet_count       AS WalletCount,
                w.last_active        AS LastActive
            FROM w
            ORDER BY w.last_active DESC
            LIMIT 500
            """;

        await using var conn = await connections.OpenAsync(ct);
        var q = (Q ?? "").Trim();
        var rows = await conn.QueryAsync<PersonRow>(new CommandDefinition(
            sql, new { q }, cancellationToken: ct));
        People = rows.ToList();
    }
}

public sealed record PersonRow(
    long UserId,
    string DisplayName,
    int WalletCount,
    DateTimeOffset LastActive);

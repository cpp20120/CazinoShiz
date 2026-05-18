using BotFramework.Host;
using BotFramework.Host.Services;
using BotFramework.Sdk;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CasinoShiz.Host.Pages.Admin;

public sealed class UsersModel(
    INpgsqlConnectionFactory connections,
    IEconomicsService economics,
    IAdminAuditLog audit) : PageModel
{
    public IReadOnlyList<UserRow> Users { get; private set; } = [];

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    public string? Flash { get; set; }
    public bool FlashError { get; set; }
    public AdminSession? Actor { get; private set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        Actor = HttpContext.Session.GetAdminSession();
        await LoadAsync(ct);
    }

    public async Task<IActionResult> OnPostSetAsync(
        long userId, long balanceScopeId, int coins, string operationId, CancellationToken ct)
    {
        var actor = HttpContext.Session.GetAdminSession();
        if (actor?.Role != AdminRole.SuperAdmin)
            return StatusCode(403);

        if (!IsValidOperationId(operationId))
        {
            TempData["FlashError"] = true;
            TempData["Flash"] = "Invalid operation id.";
            return RedirectToPage(new { q = Q });
        }

        var current = await economics.GetBalanceAsync(userId, balanceScopeId, ct);
        var d = coins - current;
        var newCoins = current;
        if (d != 0)
            newCoins = await ApplyAdminAdjustmentOnceAsync(userId, balanceScopeId, d, "admin.set", operationId, ct);

        await audit.LogAsync(actor.UserId, actor.Name, "users.set_coins",
            new { targetUserId = userId, balanceScopeId, coins, newCoins, operationId }, ct);

        TempData["Flash"] = $"User {userId} scope {balanceScopeId} → {newCoins} coins";
        return RedirectToPage(new { q = Q });
    }

    public async Task<IActionResult> OnPostAdjustAsync(
        long userId, long balanceScopeId, int delta, string operationId, CancellationToken ct)
    {
        var actor = HttpContext.Session.GetAdminSession();
        if (actor?.Role != AdminRole.SuperAdmin)
            return StatusCode(403);

        if (delta == 0)
        {
            TempData["FlashError"] = "Delta must be non-zero";
            return RedirectToPage(new { q = Q });
        }

        if (!IsValidOperationId(operationId))
        {
            TempData["FlashError"] = true;
            TempData["Flash"] = "Invalid operation id.";
            return RedirectToPage(new { q = Q });
        }

        var newCoins = await ApplyAdminAdjustmentOnceAsync(userId, balanceScopeId, delta, "admin.adjust", operationId, ct);

        await audit.LogAsync(actor.UserId, actor.Name, "users.adjust_coins",
            new { targetUserId = userId, balanceScopeId, delta, newCoins, operationId }, ct);

        TempData["Flash"] =
            $"User {userId} scope {balanceScopeId}: {(delta > 0 ? "+" : "")}{delta} → {newCoins} coins";
        return RedirectToPage(new { q = Q });
    }

    private async Task<int> ApplyAdminAdjustmentOnceAsync(
        long userId,
        long balanceScopeId,
        int delta,
        string reason,
        string operationId,
        CancellationToken ct)
    {
        const string existingSql = """
            SELECT balance_after
            FROM economics_ledger
            WHERE operation_id = @operationId
            """;
        const string selectSql = """
            SELECT coins, version FROM users
            WHERE telegram_user_id = @userId AND balance_scope_id = @balanceScopeId
            FOR UPDATE
            """;
        const string updateSql = """
            UPDATE users
            SET coins = @newCoins, version = @newVersion, updated_at = now()
            WHERE telegram_user_id = @userId AND balance_scope_id = @balanceScopeId
            """;
        const string insertLedger = """
            INSERT INTO economics_ledger (telegram_user_id, balance_scope_id, delta, balance_after, reason, operation_id)
            VALUES (@userId, @balanceScopeId, @delta, @newCoins, @reason, @operationId)
            """;

        await using var conn = await connections.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var existing = await conn.QuerySingleOrDefaultAsync<int?>(new CommandDefinition(
            existingSql,
            new { operationId },
            transaction: tx,
            cancellationToken: ct));
        if (existing.HasValue)
        {
            await tx.CommitAsync(ct);
            return existing.Value;
        }

        var row = await conn.QuerySingleOrDefaultAsync<(int coins, long version)>(new CommandDefinition(
            selectSql,
            new { userId, balanceScopeId },
            transaction: tx,
            cancellationToken: ct));
        if (row.Equals(default((int, long))))
        {
            await tx.RollbackAsync(ct);
            throw new InvalidOperationException($"User {userId} scope {balanceScopeId} not found.");
        }

        var newCoins = row.coins + delta;
        var newVersion = row.version + 1;
        await conn.ExecuteAsync(new CommandDefinition(
            updateSql,
            new { userId, balanceScopeId, newCoins, newVersion },
            transaction: tx,
            cancellationToken: ct));
        await conn.ExecuteAsync(new CommandDefinition(
            insertLedger,
            new { userId, balanceScopeId, delta, newCoins, reason, operationId },
            transaction: tx,
            cancellationToken: ct));

        await tx.CommitAsync(ct);
        return newCoins;
    }

    private static bool IsValidOperationId(string? operationId) =>
        !string.IsNullOrWhiteSpace(operationId)
        && operationId.Length <= 128
        && Guid.TryParse(operationId, out _);

    private async Task LoadAsync(CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        const string sql = """
            SELECT telegram_user_id AS UserId, balance_scope_id AS BalanceScopeId, display_name AS DisplayName,
                   coins AS Coins, created_at AS CreatedAt, updated_at AS UpdatedAt
            FROM users
            WHERE (@q = '' OR display_name ILIKE '%' || @q || '%'
                  OR telegram_user_id::text = @q
                  OR balance_scope_id::text = @q)
            ORDER BY coins DESC
            LIMIT 500
            """;
        var rows = await conn.QueryAsync<UserRow>(new CommandDefinition(sql, new { q = Q ?? "" }, cancellationToken: ct));
        Users = rows.ToList();

        Flash = TempData["Flash"] as string;
        FlashError = TempData["FlashError"] is not null;
    }
}

public sealed record UserRow(
    long UserId, long BalanceScopeId, string DisplayName, int Coins, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
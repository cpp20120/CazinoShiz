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

    public async Task<IActionResult> OnPostSetAsync(long userId, int coins, CancellationToken ct)
    {
        var actor = HttpContext.Session.GetAdminSession();
        if (actor?.Role != AdminRole.SuperAdmin)
            return StatusCode(403);

        var current = await economics.GetBalanceAsync(userId, ct);
        var delta = coins - current;
        if (delta != 0)
            await economics.AdjustUncheckedAsync(userId, delta, ct);

        await audit.LogAsync(actor.UserId, actor.Name, "users.set_coins",
            new { targetUserId = userId, coins }, ct);

        TempData["Flash"] = $"User {userId} → {coins} coins";
        return RedirectToPage(new { q = Q });
    }

    public async Task<IActionResult> OnPostAdjustAsync(long userId, int delta, CancellationToken ct)
    {
        var actor = HttpContext.Session.GetAdminSession();
        if (actor?.Role != AdminRole.SuperAdmin)
            return StatusCode(403);

        if (delta == 0)
        {
            TempData["FlashError"] = "Delta must be non-zero";
            return RedirectToPage(new { q = Q });
        }

        await economics.AdjustUncheckedAsync(userId, delta, ct);
        var newCoins = await economics.GetBalanceAsync(userId, ct);

        await audit.LogAsync(actor.UserId, actor.Name, "users.adjust_coins",
            new { targetUserId = userId, delta, newCoins }, ct);

        TempData["Flash"] = $"User {userId}: {(delta > 0 ? "+" : "")}{delta} → {newCoins} coins";
        return RedirectToPage(new { q = Q });
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        const string sql = """
            SELECT telegram_user_id AS UserId, display_name AS DisplayName,
                   coins AS Coins, created_at AS CreatedAt, updated_at AS UpdatedAt
            FROM users
            WHERE (@q = '' OR display_name ILIKE '%' || @q || '%' OR telegram_user_id::text = @q)
            ORDER BY coins DESC
            LIMIT 500
            """;
        var rows = await conn.QueryAsync<UserRow>(new CommandDefinition(sql, new { q = Q ?? "" }, cancellationToken: ct));
        Users = rows.ToList();

        Flash = TempData["Flash"] as string;
        FlashError = TempData["FlashError"] is not null;
    }
}

public sealed record UserRow(long UserId, string DisplayName, int Coins, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

using BotFramework.Host;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CasinoShiz.Host.Pages.Admin;

public sealed class UsersModel(INpgsqlConnectionFactory connections) : PageModel
{
    public IReadOnlyList<UserRow> Users { get; private set; } = [];

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    public string? Flash { get; set; }
    public bool FlashError { get; set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        await LoadAsync(ct);
    }

    public async Task<IActionResult> OnPostSetAsync(long userId, int coins, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        var rows = await conn.ExecuteAsync(new CommandDefinition("""
            UPDATE users
            SET coins = @coins, version = version + 1, updated_at = now()
            WHERE telegram_user_id = @userId
            """,
            new { userId, coins }, cancellationToken: ct));
        TempData["Flash"] = rows > 0 ? $"User {userId} → {coins} coins" : $"User {userId} not found";
        if (rows == 0) TempData["FlashError"] = "x";
        return RedirectToPage(new { q = Q });
    }

    public async Task<IActionResult> OnPostAdjustAsync(long userId, int delta, CancellationToken ct)
    {
        if (delta == 0)
        {
            TempData["FlashError"] = "Delta must be non-zero";
            return RedirectToPage(new { q = Q });
        }
        await using var conn = await connections.OpenAsync(ct);
        var newCoins = await conn.ExecuteScalarAsync<int?>(new CommandDefinition("""
            UPDATE users
            SET coins = coins + @delta, version = version + 1, updated_at = now()
            WHERE telegram_user_id = @userId
            RETURNING coins
            """,
            new { userId, delta }, cancellationToken: ct));
        TempData["Flash"] = newCoins.HasValue
            ? $"User {userId}: {(delta > 0 ? "+" : "")}{delta} → {newCoins} coins"
            : $"User {userId} not found";
        if (newCoins is null) TempData["FlashError"] = "x";
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

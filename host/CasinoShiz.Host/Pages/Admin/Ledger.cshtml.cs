using BotFramework.Host;
using BotFramework.Host.Services;
using BotFramework.Sdk;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CasinoShiz.Host.Pages.Admin;

public sealed class LedgerModel(
    INpgsqlConnectionFactory connections,
    IEconomicsService economics,
    IAdminAuditLog audit) : PageModel
{
    public IReadOnlyList<LedgerRow> Rows { get; private set; } = [];
    public AdminSession? Actor { get; private set; }
    public bool CanRevert { get; private set; }

    [BindProperty(SupportsGet = true)]
    public string? U { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? S { get; set; }

    public string? Flash { get; set; }
    public bool FlashError { get; set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        Actor = HttpContext.Session.GetAdminSession();
        CanRevert = Actor?.Role == AdminRole.SuperAdmin;
        await LoadAsync(ct);
        Flash = TempData["Flash"] as string;
        FlashError = TempData["FlashError"] is not null;
    }

    public async Task<IActionResult> OnPostRevertAsync(long ledgerId, CancellationToken ct)
    {
        var actor = HttpContext.Session.GetAdminSession();
        if (actor?.Role != AdminRole.SuperAdmin)
            return Forbid();

        try
        {
            var result = await economics.RevertLedgerEntryAsync(ledgerId, ct);
            switch (result.Status)
            {
                case LedgerRevertStatus.Ok:
                    await audit.LogAsync(actor.UserId, actor.Name, "ledger.revert", new
                    {
                        economicsLedgerId = ledgerId,
                        newBalance = result.NewBalance,
                    }, ct);
                    TempData["Flash"] = $"Reverted ledger line #{ledgerId} — balance now {result.NewBalance}.";
                    break;
                case LedgerRevertStatus.AlreadyReverted:
                    TempData["FlashError"] = true;
                    TempData["Flash"] = $"Line #{ledgerId} was already reverted.";
                    break;
                case LedgerRevertStatus.NotFound:
                    TempData["FlashError"] = true;
                    TempData["Flash"] = $"Ledger line #{ledgerId} not found.";
                    break;
                case LedgerRevertStatus.UserMissing:
                    TempData["FlashError"] = true;
                    TempData["Flash"] = $"User row missing for line #{ledgerId} (cannot apply correction).";
                    break;
                case LedgerRevertStatus.NoEffect:
                    TempData["Flash"] = $"Line #{ledgerId} has zero delta — nothing to reverse.";
                    break;
                case LedgerRevertStatus.CorrectionOutOfRange:
                    TempData["FlashError"] = true;
                    TempData["Flash"] =
                        $"Line #{ledgerId} cannot be reverted automatically (delta out of reversible range).";
                    break;
                default:
                    TempData["FlashError"] = true;
                    TempData["Flash"] = "Revert failed.";
                    break;
            }
        }
        catch (Exception ex)
        {
            TempData["FlashError"] = true;
            TempData["Flash"] = $"Revert error: {ex.Message}";
        }

        return RedirectToPage(new { U, S });
    }

    private async Task LoadAsync(CancellationToken ct)
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

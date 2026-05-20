using BotFramework.Host;
using BotFramework.Sdk;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CasinoShiz.Host.Pages.Admin;

public sealed class MetaAlertsModel(
    INpgsqlConnectionFactory connections,
    IAdminAuditLog audit) : PageModel
{
    public IReadOnlyList<MetaAlertRow> Rows { get; private set; } = [];
    public AdminSession? Actor { get; private set; }
    public bool CanUpdate { get; private set; }

    [BindProperty(SupportsGet = true)] public string? ChatId { get; set; }
    [BindProperty(SupportsGet = true)] public string Status { get; set; } = "open";
    [BindProperty(SupportsGet = true)] public string? UserId { get; set; }

    public string? Flash { get; set; }
    public bool FlashError { get; set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        Actor = HttpContext.Session.GetAdminSession();
        if (Actor is null) return RedirectToPage("/Admin/Login");

        CanUpdate = Actor.Role == AdminRole.SuperAdmin;
        await LoadAsync(ct);
        Flash = TempData["Flash"] as string;
        FlashError = TempData["FlashError"] is not null;
        return Page();
    }

    public async Task<IActionResult> OnPostUpdateAsync(long flagId, string targetStatus, CancellationToken ct)
    {
        var actor = HttpContext.Session.GetAdminSession();
        if (actor?.Role != AdminRole.SuperAdmin) return Forbid();

        targetStatus = NormalizeStatus(targetStatus);
        if (targetStatus is not ("resolved" or "ignored"))
        {
            TempData["FlashError"] = true;
            TempData["Flash"] = "Invalid target status.";
            return RedirectToPage(new { ChatId, Status, UserId });
        }

        const string sql = """
            UPDATE meta_risk_flags
            SET status = @targetStatus,
                resolved_at = now(),
                updated_at = now()
            WHERE id = @flagId AND status = 'open'
            """;

        await using var conn = await connections.OpenAsync(ct);
        var changed = await conn.ExecuteAsync(new CommandDefinition(sql, new { flagId, targetStatus }, cancellationToken: ct));

        if (changed > 0)
        {
            await audit.LogAsync(actor.UserId, actor.Name, "meta_alert.update", new { flagId, targetStatus }, ct);
            TempData["Flash"] = $"Alert #{flagId} marked as {targetStatus}.";
        }
        else
        {
            TempData["FlashError"] = true;
            TempData["Flash"] = $"Open alert #{flagId} not found.";
        }

        return RedirectToPage(new { ChatId, Status, UserId });
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        long? chatId = long.TryParse(ChatId, out var cid) ? cid : null;
        long? userId = long.TryParse(UserId, out var uid) ? uid : null;
        var status = NormalizeStatus(Status);
        if (status is not ("open" or "resolved" or "ignored" or "all")) status = "open";
        Status = status;

        const string sql = """
            SELECT id AS Id,
                   season_id AS SeasonId,
                   chat_id AS ChatId,
                   user_id AS UserId,
                   display_name AS DisplayName,
                   kind AS Kind,
                   severity AS Severity,
                   status AS Status,
                   reason AS Reason,
                   evidence::text AS EvidenceJson,
                   created_at AS CreatedAt,
                   updated_at AS UpdatedAt,
                   resolved_at AS ResolvedAt
            FROM meta_risk_flags
            WHERE (@chatId IS NULL OR chat_id = @chatId)
              AND (@userId IS NULL OR user_id = @userId)
              AND (@status = 'all' OR status = @status)
            ORDER BY created_at DESC
            LIMIT 500
            """;

        await using var conn = await connections.OpenAsync(ct);
        var rows = await conn.QueryAsync<MetaAlertRow>(new CommandDefinition(sql, new { chatId, userId, status }, cancellationToken: ct));
        Rows = rows.ToList();
    }

    private static string NormalizeStatus(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "ignore" => "ignored",
        "ignored" => "ignored",
        "resolve" => "resolved",
        "resolved" => "resolved",
        "all" => "all",
        _ => "open",
    };
}

public sealed record MetaAlertRow(
    long Id,
    long SeasonId,
    long ChatId,
    long UserId,
    string DisplayName,
    string Kind,
    string Severity,
    string Status,
    string Reason,
    string EvidenceJson,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ResolvedAt);

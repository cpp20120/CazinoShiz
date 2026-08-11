using BotFramework.Contracts.Operations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CasinoShiz.AdminBff.Pages;

public sealed class OperationsModel(IOperationsAdminService operations) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? EventType { get; set; }
    [BindProperty(SupportsGet = true)] public string? OutboxStatus { get; set; }
    [BindProperty(SupportsGet = true)] public string? OperationId { get; set; }
    public IReadOnlyList<OperationFailure> Failures { get; private set; } = [];
    public IReadOnlyList<OperationOutbox> Outbox { get; private set; } = [];
    public IReadOnlyList<OperationJob> Jobs { get; private set; } = [];
    public WagerWorkflowTimeline? WagerTimeline { get; private set; }
    public string? Error { get; private set; }
    public string? Flash { get; private set; }
    public bool FlashError { get; private set; }
    public bool CanMutate => HttpContext.Session.IsSuperAdmin();

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (!HttpContext.Session.IsAuthenticated())
            return RedirectToPage("/Login");

        try
        {
            Failures = await operations.ListFailuresAsync(100, Normalize(EventType), ct);
            Outbox = await operations.ListOutboxAsync(100, Normalize(OutboxStatus), ct);
            Jobs = await operations.ListJobsAsync(ct);
            if (!string.IsNullOrWhiteSpace(OperationId))
                WagerTimeline = await operations.GetWagerTimelineAsync(OperationId, ct);
        }
        catch (Exception ex)
        {
            Error = $"Operations service unavailable: {ex.GetType().Name}";
        }

        Flash = TempData["flash"] as string;
        FlashError = TempData["error"] is true;
        return Page();
    }

    public Task<IActionResult> OnPostRetryEventAsync(long id, CancellationToken ct) =>
        Mutate(() => operations.RetryEventAsync(id, HttpContext.Session.ActorId(), HttpContext.Session.ActorName(), ct));

    public Task<IActionResult> OnPostRescheduleOutboxAsync(long id, CancellationToken ct) =>
        Mutate(() => operations.RescheduleOutboxAsync(id, HttpContext.Session.ActorId(), HttpContext.Session.ActorName(), ct));

    private async Task<IActionResult> Mutate(Func<Task<OperationMutationResult>> action)
    {
        if (!HttpContext.Session.IsAuthenticated())
            return RedirectToPage("/Login");
        if (!HttpContext.Session.IsSuperAdmin())
            return Forbid();

        var result = await action();
        TempData["flash"] = result.Message;
        TempData["error"] = !result.Success;
        return RedirectToPage(new { EventType, OutboxStatus, OperationId });
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

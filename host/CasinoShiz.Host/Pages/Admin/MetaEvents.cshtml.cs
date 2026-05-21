using BotFramework.Host.Services;
using Games.Meta;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CasinoShiz.Host.Pages.Admin;

public sealed class MetaEventsModel(IMetaHistoryStore history) : PageModel
{
    public IReadOnlyList<MetaHistoryEvent> Events { get; private set; } = [];
    public MetaHistoryStats Stats { get; private set; } = new(0, 0, 0, null, null);

    [BindProperty(SupportsGet = true)] public string? EventType { get; set; }
    [BindProperty(SupportsGet = true)] public string? AggregateType { get; set; }
    [BindProperty(SupportsGet = true)] public string? AggregateId { get; set; }
    [BindProperty(SupportsGet = true)] public string? ChatId { get; set; }
    [BindProperty(SupportsGet = true)] public string? UserId { get; set; }
    [BindProperty(SupportsGet = true)] public int Limit { get; set; } = 200;

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        var actor = HttpContext.Session.GetAdminSession();
        if (actor is null) return RedirectToPage("/Admin/Login");

        long? chatId = long.TryParse(ChatId, out var cid) ? cid : null;
        long? userId = long.TryParse(UserId, out var uid) ? uid : null;
        Limit = Math.Clamp(Limit, 1, 1000);

        Stats = await history.GetStatsAsync(ct);
        Events = await history.ListAsync(EventType, AggregateType, AggregateId, chatId, userId, Limit, ct);
        return Page();
    }
}

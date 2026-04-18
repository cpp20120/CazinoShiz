using CasinoShiz.Services.Admin;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CasinoShiz.Pages.Admin;

public class IndexModel(AdminService admin) : PageModel
{
    public string? Search { get; private set; }
    public UserListResult Result { get; private set; } = new([], 0, 0, 50);
    public OverviewStats Stats { get; private set; } = new(0, 0, 0, 0, 0);

    public async Task OnGetAsync(string? q, CancellationToken ct)
    {
        Search = q;
        Result = await admin.ListUsersAsync(q, 0, 50, ct);
        Stats = await admin.GetOverviewStatsAsync(ct);
    }
}

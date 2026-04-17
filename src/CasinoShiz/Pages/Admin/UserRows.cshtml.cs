using CasinoShiz.Services.Admin;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CasinoShiz.Pages.Admin;

public class UserRowsModel(AdminService admin) : PageModel
{
    public UserListResult Result { get; private set; } = new([], 0, 0, 50);

    public async Task OnGetAsync(string? q, CancellationToken ct)
    {
        Result = await admin.ListUsersAsync(q, 0, 50, ct);
    }
}

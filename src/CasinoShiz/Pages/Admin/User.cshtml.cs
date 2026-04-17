using CasinoShiz.Services.Admin;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CasinoShiz.Pages.Admin;

public class UserDetailModel(AdminService admin) : PageModel
{
    public long Id { get; private set; }
    public UserDetail? Detail { get; private set; }

    public async Task OnGetAsync(long id, CancellationToken ct)
    {
        Id = id;
        Detail = await admin.GetUserDetailAsync(id, ct);
    }
}

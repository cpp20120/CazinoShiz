using CasinoShiz.Services.Admin;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CasinoShiz.Pages.Admin;

public class ResetAttemptsModel(AdminService admin) : PageModel
{
    public async Task<IActionResult> OnPostAsync(long id, CancellationToken ct)
    {
        await admin.ResetSlotAttemptsAsync(callerId: -1, targetUserId: id, ct);
        return Redirect($"/admin/user/{id}");
    }
}

using CasinoShiz.Services.Admin;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CasinoShiz.Pages.Admin;

public class SecretHitlerCancelModel(AdminService admin) : PageModel
{
    public async Task<IActionResult> OnPostAsync(string code, CancellationToken ct)
    {
        await admin.CancelSecretHitlerRoomAsync(callerId: -1, inviteCode: code, ct);
        return Redirect("/admin/sh");
    }
}

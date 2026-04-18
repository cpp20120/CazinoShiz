using CasinoShiz.Services.Admin;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CasinoShiz.Pages.Admin;

public class SecretHitlerRoomModel(AdminService admin) : PageModel
{
    public string Code { get; private set; } = "";
    public ShRoomDetailView? View { get; private set; }

    public async Task OnGetAsync(string code, CancellationToken ct)
    {
        Code = code;
        View = await admin.GetSecretHitlerRoomAsync(code, ct);
    }
}

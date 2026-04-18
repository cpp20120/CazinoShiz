using CasinoShiz.Services.Admin;
using CasinoShiz.Services.Horse;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CasinoShiz.Pages.Admin;

public class HorseModel(AdminService admin) : PageModel
{
    public HorseRaceAdminView? View { get; private set; }
    public HorseRunAdminResult? LastRun { get; private set; }
    public string? GifDataUrl { get; private set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        View = await admin.GetHorseRaceViewAsync(ct);
    }

    public async Task<IActionResult> OnPostRunAsync(CancellationToken ct)
    {
        LastRun = await admin.RunHorseRaceAsync(callerId: 0, ct);
        if (LastRun.Error == HorseError.None && LastRun.GifBytes is { Length: > 0 } bytes)
            GifDataUrl = "data:image/gif;base64," + Convert.ToBase64String(bytes);
        View = await admin.GetHorseRaceViewAsync(ct);
        return Page();
    }
}

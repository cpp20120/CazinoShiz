using CasinoShiz.Data;
using CasinoShiz.Services.Admin;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace CasinoShiz.Pages.Admin;

[IgnoreAntiforgeryToken]
public class RenameModel(AdminService admin, AppDbContext db) : PageModel
{
    public string Status { get; private set; } = "";
    public string OldName { get; private set; } = "";
    public string NewName { get; private set; } = "";

    public async Task<IActionResult> OnPostAsync(long id, [FromForm] string newName, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.TelegramUserId == id, ct);
        if (user == null)
        {
            Status = "missing";
            return Page();
        }

        OldName = user.DisplayName;
        NewName = newName?.Trim() ?? "";

        if (string.IsNullOrEmpty(NewName))
        {
            Status = "nochange";
            return Page();
        }

        var result = await admin.RenameAsync(OldName, NewName, ct);
        Status = result.Op switch
        {
            RenameOp.Set => "set",
            RenameOp.Cleared => "cleared",
            RenameOp.NoChange => "nochange",
            _ => "unknown",
        };
        return Page();
    }
}

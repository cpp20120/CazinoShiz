using CasinoShiz.Data;
using CasinoShiz.Data.Entities;
using CasinoShiz.Services.Admin;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace CasinoShiz.Pages.Admin;

[IgnoreAntiforgeryToken]
public class PayModel(AdminService admin, AppDbContext db) : PageModel
{
    public UserState? Target { get; private set; }

    public async Task<IActionResult> OnPostAsync(long id, [FromForm] int amount, CancellationToken ct)
    {
        if (amount != 0)
            await admin.PayAsync(callerId: -1, targetUserId: id, amount: amount, ct);

        Target = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.TelegramUserId == id, ct);
        return Page();
    }
}

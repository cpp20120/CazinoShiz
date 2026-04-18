using CasinoShiz.Services.Admin;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CasinoShiz.Pages.Admin;

public class SecretHitlerModel(AdminService admin) : PageModel
{
    public IReadOnlyList<ShRoomListItem> Rooms { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken ct)
    {
        var list = await admin.ListSecretHitlerRoomsAsync(ct);
        Rooms = list.Rooms;
    }
}

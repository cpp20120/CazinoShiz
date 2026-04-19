using Games.Horse;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CasinoShiz.Host.Pages.Admin;

public sealed class HorseImageModel(
    IHorseResultStore results,
    HorseGifCache gifCache) : PageModel
{
    public async Task<IActionResult> OnGetAsync(string date, string? kind, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(date)) return NotFound();

        if (kind == "gif")
        {
            var bytes = gifCache.Get(date);
            if (bytes is null) return NotFound();
            return File(bytes, "image/gif");
        }

        var row = await results.FindAsync(date, ct);
        if (row is null) return NotFound();
        return File(row.ImageData, "image/png");
    }
}

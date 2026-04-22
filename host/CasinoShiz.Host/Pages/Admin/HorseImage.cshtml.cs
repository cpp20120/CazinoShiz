using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CasinoShiz.Host.Pages.Admin;

public sealed class HorseImageModel(HorseGifCache gifCache) : PageModel
{
    public async Task<IActionResult> OnGetAsync(string date, string? kind, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(date)) return NotFound();

        if (kind == "gif")
        {
            var bytes = await gifCache.GetAsync(date, ct);
            if (bytes is null) return NotFound();
            return File(bytes, "image/gif");
        }

        return NotFound();
    }
}
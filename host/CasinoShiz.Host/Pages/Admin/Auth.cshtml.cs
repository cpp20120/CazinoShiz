using BotFramework.Host.Composition;
using BotFramework.Host.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace CasinoShiz.Host.Pages.Admin;

public sealed class AuthModel(
    TelegramLoginVerifier verifier,
    IOptions<BotFrameworkOptions> opts) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public IActionResult OnGet()
    {
        var fields = Request.Query.ToDictionary(kv => kv.Key, kv => kv.Value.ToString());

        if (!verifier.Verify(fields, out var userId, out var name))
            return RedirectToPage("Login", new { returnUrl = ReturnUrl });

        var botOpts = opts.Value;
        AdminRole role;
        if (botOpts.Admins.Contains(userId))
            role = AdminRole.SuperAdmin;
        else if (botOpts.ReadOnlyAdmins.Contains(userId))
            role = AdminRole.ReadOnly;
        else
            return StatusCode(403, "Access denied");

        HttpContext.Session.SetAdminSession(new AdminSession(userId, name, role));

        var target = ReturnUrl is { Length: > 0 } r && r.StartsWith('/') ? r : "/admin";
        return Redirect(target);
    }
}

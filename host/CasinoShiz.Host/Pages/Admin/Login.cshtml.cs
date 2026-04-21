using System.Security.Cryptography;
using System.Text;
using BotFramework.Host.Composition;
using BotFramework.Host.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace CasinoShiz.Host.Pages.Admin;

public sealed class LoginModel(IOptions<BotFrameworkOptions> opts) : PageModel
{
    public string BotUsername { get; private set; } = "";
    public bool TokenLoginEnabled { get; private set; }
    public string? Error { get; private set; }

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public IActionResult OnGet()
    {
        Populate();
        return Page();
    }

    public IActionResult OnPost(string? token)
    {
        Populate();

        var o = opts.Value;
        if (string.IsNullOrEmpty(o.AdminWebToken) || string.IsNullOrEmpty(token))
        {
            Error = "Invalid token";
            return Page();
        }

        var a = Encoding.UTF8.GetBytes(token);
        var b = Encoding.UTF8.GetBytes(o.AdminWebToken);
        if (a.Length != b.Length || !CryptographicOperations.FixedTimeEquals(a, b))
        {
            Error = "Invalid token";
            return Page();
        }

        if (!o.Admins.Any())
        {
            Error = "Bot:Admins is empty — no user to log in as";
            return Page();
        }

        // Token login: grant SuperAdmin as first configured admin
        HttpContext.Session.SetAdminSession(new AdminSession(o.Admins[0], "token-admin", AdminRole.SuperAdmin));

        var target = ReturnUrl is { Length: > 0 } r && r.StartsWith('/') ? r : "/admin";
        return Redirect(target);
    }

    private void Populate()
    {
        BotUsername = opts.Value.Username.TrimStart('@');
        TokenLoginEnabled = !string.IsNullOrEmpty(opts.Value.AdminWebToken);
    }
}

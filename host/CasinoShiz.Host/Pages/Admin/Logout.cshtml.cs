using BotFramework.Host.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CasinoShiz.Host.Pages.Admin;

public sealed class LogoutModel : PageModel
{
    public IActionResult OnGet()
    {
        HttpContext.Session.ClearAdminSession();
        return RedirectToPage("Login");
    }

    public IActionResult OnPost()
    {
        HttpContext.Session.ClearAdminSession();
        return RedirectToPage("Login");
    }
}

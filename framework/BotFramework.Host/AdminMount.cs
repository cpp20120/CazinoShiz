// ─────────────────────────────────────────────────────────────────────────────
// AdminMount — how the Host wires module-contributed admin pages.
//
// Routing shape:
//   /admin                         — Host-owned dashboard; lists modules
//   /admin/<moduleId>              — module index page (Route == "")
//   /admin/<moduleId>/<route>      — any other page the module declared
//
// Authentication stays where it already is: the admin middleware validates
// AdminWebToken before the handler here ever runs. Modules never see tokens.
//
// The Host renders the chrome (navbar, nav items aggregated from every
// module's IAdminMenu list, logout link). The module body is inserted as
// a trusted HTML fragment — modules are first-party code, same trust
// boundary as the Host itself, so no sanitization step is added here. If
// a future loader supports untrusted modules, this is where a sandbox goes.
// ─────────────────────────────────────────────────────────────────────────────

using BotFramework.Sdk;

namespace BotFramework.Host;

public sealed class AdminMount(
    IReadOnlyList<IModule> modules,
    IReadOnlyDictionary<string, IReadOnlyList<IAdminPage>> pagesByModule,
    IReadOnlyList<AdminMenu> menus)
{
    public async Task<AdminResponse> DispatchAsync(
        string moduleId,
        string route,
        IReadOnlyDictionary<string, string> query,
        IServiceProvider scope,
        CancellationToken ct)
    {
        if (!pagesByModule.TryGetValue(moduleId, out var pages))
            return new AdminResponse("module not found", 404);

        var page = pages.FirstOrDefault(p => p.Route == route);
        if (page is null) return new AdminResponse("page not found", 404);

        var body = await page.RenderAsync(new AdminRequest(query, scope), ct);
        var html = WrapInChrome(body.Html, moduleId, page.Title);
        return new AdminResponse(html, body.StatusCode);
    }

    private string WrapInChrome(string body, string moduleId, string title)
    {
        // Real impl renders a Razor layout with the menu list. Sketch just shows
        // the wrapping happens somewhere.
        return $"<!-- admin: {moduleId}/{title} -->\n{body}";
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Admin UI contribution — how a module ships its own pages under the Host's
// admin shell. The Host owns authentication (AdminWebToken), the layout
// chrome, and routing; the module owns the content for /admin/<moduleId>/*.
//
// Why this matters: operators need per-game admin views — "active poker
// tables", "recent SH rooms", "issued freespin codes". Today CasinoShiz bakes
// all of them into one Pages/Admin tree inside the monolith. If a module
// can't ship its own pages, every new game forces a change in the Host
// project — exactly the coupling we're trying to break.
//
// Two options were considered:
//
// Tier 1 (in this sketch): pages are plain data contributions. The module
//   declares `IAdminPage` instances with (route, title, renderer). Host
//   mounts them under /admin/<moduleId>/<route> and asks the renderer to
//   produce HTML. Good for lists and status dashboards; awkward for forms.
//
// Tier 2 (deferred): module ships a Razor Class Library (RCL). The Host
//   references the RCL; ASP.NET Razor autodiscovers .cshtml files. This is
//   the right fit for complex UI but forces every module to pick up a
//   Microsoft.AspNetCore.* dep — heavy for a Dice module whose admin page
//   is two counters.
//
// Starting with Tier 1 keeps the contract small. Modules that want Razor
// opt into Tier 2 with a separate "<Module>.Admin.csproj" companion. See
// games/SecretHitler/SecretHitler.Admin/ for the pattern.
//
// Tier 2 opt-in is a marker interface: IRazorAdminModule. The Host looks at
// every IModule at startup, and for any implementing IRazorAdminModule it
// adds that RCL assembly to its Razor Pages application part. From that
// point on the RCL's .cshtml files are routable under /admin/<module>/...
// just like pages that live in the Host project itself.
// ─────────────────────────────────────────────────────────────────────────────

namespace BotFramework.Sdk;

public interface IAdminPage
{
    /// Path under the module's admin root. "" = index, "rooms" → /admin/sh/rooms.
    string Route { get; }

    /// Shown in the module's admin nav bar.
    string Title { get; }

    /// Called per-request. Receives parsed query params + the module's services
    /// (resolved from the current scope) and returns the rendered HTML body.
    /// Host wraps it in the shared admin chrome and sets Content-Type.
    Task<AdminResponse> RenderAsync(AdminRequest request, CancellationToken ct);
}

public sealed record AdminRequest(
    IReadOnlyDictionary<string, string> Query,
    IServiceProvider Services);

public sealed record AdminResponse(string Html, int StatusCode = 200);

/// Optional menu entry surfaced in the Host's top-level admin nav. A module
/// with multiple pages registers one AdminMenu per section (e.g. "Secret
/// Hitler › Active rooms", "Secret Hitler › Event log").
public sealed record AdminMenu(string ModuleId, string Title, string DefaultRoute);

/// Tier 2 marker — module ships a Razor Class Library with .cshtml pages.
/// The Host calls AdminAssembly on each IRazorAdminModule it sees and adds
/// the returned assembly to its Razor application parts so the RCL's pages
/// become routable. Area name is conventionally the module Id in PascalCase
/// (e.g. module "sh" → RCL Area "Sh" → URL /admin/sh/…).
public interface IRazorAdminModule
{
    /// The RCL assembly that carries the .cshtml pages. Typically
    /// `typeof(MyAdminPageModel).Assembly`.
    System.Reflection.Assembly AdminAssembly { get; }
}

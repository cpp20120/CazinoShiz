// ─────────────────────────────────────────────────────────────────────────────
// Middleware pipeline contracts.
//
// The order middleware registers in is the order it wraps, onion-style:
//
//   request  → [logging] → [metrics] → [auth] → [rate-limit] → [validation] → handler
//   response ← [logging] ← [metrics] ← [auth] ← [rate-limit] ← [validation] ← handler
//
// Each middleware gets next() and decides whether to call it, short-circuit,
// wrap in try/catch, or transform. Familiar pattern from ASP.NET Core.
// Modules can contribute middleware too — but most cross-cutting concerns
// are Host-owned so a new game doesn't accidentally reorder the chain.
// ─────────────────────────────────────────────────────────────────────────────

namespace BotFramework.Sdk;
/// Carries the command plus out-of-band data middleware wants to stash.
/// Items is a loose dictionary — typed accessors live in middleware-specific
/// extensions so the surface here stays minimal.
public sealed class CommandContext(ICommand command, RequestContext request, CancellationToken ct)
{
    public ICommand Command { get; } = command;
    public CancellationToken Cancellation { get; } = ct;
    public IDictionary<string, object?> Items { get; } = new Dictionary<string, object?>();

    /// Populated by the Host before dispatch — user id parsed from the
    /// incoming Update, culture, trace id. Middleware uses it for auth
    /// decisions and logging.
    public RequestContext Request { get; } = request;
}

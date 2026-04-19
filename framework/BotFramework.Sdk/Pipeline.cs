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

public interface ICommandMiddleware
{
    /// Called for every command flowing through the bus. Call next() to
    /// forward; skip it to short-circuit (e.g. rate-limit rejection).
    Task InvokeAsync(CommandContext ctx, Func<Task> next);
}

/// Carries the command plus out-of-band data middleware wants to stash.
/// Items is a loose dictionary — typed accessors live in middleware-specific
/// extensions so the surface here stays minimal.
public sealed class CommandContext
{
    public ICommand Command { get; }
    public CancellationToken Cancellation { get; }
    public IDictionary<string, object?> Items { get; } = new Dictionary<string, object?>();

    /// Populated by the Host before dispatch — user id parsed from the
    /// incoming Update, culture, trace id. Middleware uses it for auth
    /// decisions and logging.
    public RequestContext Request { get; }

    public CommandContext(ICommand command, RequestContext request, CancellationToken ct)
    {
        Command = command;
        Request = request;
        Cancellation = ct;
    }
}

/// Request-scoped identity + tracing metadata. Propagated through the bus so
/// every middleware sees the same view. Built once per incoming Telegram
/// update (or admin HTTP request) by the Host before any command dispatches.
public sealed record RequestContext(
    long UserId,
    string CultureCode,
    string TraceId,
    IReadOnlyDictionary<string, string> Tags);

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

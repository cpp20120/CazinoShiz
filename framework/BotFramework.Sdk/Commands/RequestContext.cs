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
/// Request-scoped identity + tracing metadata. Propagated through the bus so
/// every middleware sees the same view. Built once per incoming Telegram
/// update (or admin HTTP request) by the Host before any command dispatches.
public sealed record RequestContext(
    long UserId,
    string CultureCode,
    string TraceId,
    IReadOnlyDictionary<string, string> Tags);

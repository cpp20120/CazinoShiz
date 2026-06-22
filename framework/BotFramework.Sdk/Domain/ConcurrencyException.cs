// ─────────────────────────────────────────────────────────────────────────────
// Aggregate + persistence abstractions.
//
// Application services depend on IRepository<T>. The concrete repo might be
// EF-backed (classical) or event-store-backed (event-sourced) — services don't
// care. That's the whole point: persistence choice is a module decision, not a
// service decision.
// ─────────────────────────────────────────────────────────────────────────────

namespace BotFramework.Sdk;
public sealed class ConcurrencyException(string streamId, long expected, long actual)
    : Exception($"Stream {streamId} expected version {expected}, was {actual}");
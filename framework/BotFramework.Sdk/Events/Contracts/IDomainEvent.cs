// ─────────────────────────────────────────────────────────────────────────────
// Aggregate + persistence abstractions.
//
// Application services depend on IRepository<T>. The concrete repo might be
// EF-backed (classical) or event-store-backed (event-sourced) — services don't
// care. That's the whole point: persistence choice is a module decision, not a
// service decision.
// ─────────────────────────────────────────────────────────────────────────────

namespace BotFramework.Sdk;
public interface IDomainEvent
{
    /// Fully-qualified event name, e.g. "sh.chancellor_nominated". Stable across
    /// versions — used as the ClickHouse EventType *and* the event-store row's
    /// event_type column.
    string EventType { get; }

    /// Milliseconds since Unix epoch. Captured at Apply time, immutable.
    long OccurredAt { get; }
}

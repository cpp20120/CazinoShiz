// ─────────────────────────────────────────────────────────────────────────────
// Aggregate + persistence abstractions.
//
// Application services depend on IRepository<T>. The concrete repo might be
// EF-backed (classical) or event-store-backed (event-sourced) — services don't
// care. That's the whole point: persistence choice is a module decision, not a
// service decision.
// ─────────────────────────────────────────────────────────────────────────────

namespace BotFramework.Sdk;
// ─────────────────────────────────────────────────────────────────────────────

public interface IRepository<TAggregate> where TAggregate : IAggregateRoot
{
    /// Load current state of the aggregate, or null if it doesn't exist.
    Task<TAggregate?> FindAsync(string id, CancellationToken ct);

    /// Persist the aggregate. For classical aggregates this is an upsert.
    /// For event-sourced aggregates this appends PendingEvents at the given
    /// ExpectedVersion (optimistic concurrency) and clears the pending list.
    Task SaveAsync(TAggregate aggregate, CancellationToken ct);
}

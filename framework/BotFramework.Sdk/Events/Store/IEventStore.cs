// ─────────────────────────────────────────────────────────────────────────────
// Aggregate + persistence abstractions.
//
// Application services depend on IRepository<T>. The concrete repo might be
// EF-backed (classical) or event-store-backed (event-sourced) — services don't
// care. That's the whole point: persistence choice is a module decision, not a
// service decision.
// ─────────────────────────────────────────────────────────────────────────────

namespace BotFramework.Sdk;
/// Event-store contract. Typical implementation: a Postgres table
///   (stream_id TEXT, version BIGINT, event_type TEXT, payload JSONB,
///    occurred_at BIGINT, PRIMARY KEY (stream_id, version))
/// with an exclusive row-level lock on the last version at append time.
public interface IEventStore
{
    /// Returns all events in the stream, ordered by version ascending.
    Task<IReadOnlyList<StoredEvent>> LoadAsync(string streamId, CancellationToken ct);

    /// Appends events. Throws ConcurrencyException if the stream's current
    /// version doesn't match expectedVersion.
    Task AppendAsync(
        string streamId,
        long expectedVersion,
        IEnumerable<IDomainEvent> events,
        CancellationToken ct);
}

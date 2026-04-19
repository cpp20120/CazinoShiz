// ─────────────────────────────────────────────────────────────────────────────
// Aggregate + persistence abstractions.
//
// Application services depend on IRepository<T>. The concrete repo might be
// EF-backed (classical) or event-store-backed (event-sourced) — services don't
// care. That's the whole point: persistence choice is a module decision, not a
// service decision.
// ─────────────────────────────────────────────────────────────────────────────

namespace BotFramework.Sdk;

public interface IAggregateRoot
{
    string Id { get; }
}

/// Opt-in extension for aggregates that want event sourcing. Classical
/// aggregates simply implement IAggregateRoot and are persisted as rows.
public interface IEventSourcedAggregate : IAggregateRoot
{
    /// Monotonic version. Incremented each time an event is applied. Used as
    /// the optimistic-concurrency token against the event store.
    long Version { get; }

    /// Events the aggregate has emitted since load/construction that haven't
    /// been persisted yet. The repository reads this on SaveAsync and appends
    /// to the event store, then calls MarkEventsCommitted.
    IReadOnlyList<IDomainEvent> PendingEvents { get; }

    void MarkEventsCommitted();

    /// Replays a stream of events to rebuild state. Called once at load.
    void LoadFromHistory(IEnumerable<IDomainEvent> history);
}

public interface IDomainEvent
{
    /// Fully-qualified event name, e.g. "sh.chancellor_nominated". Stable across
    /// versions — used as the ClickHouse EventType *and* the event-store row's
    /// event_type column.
    string EventType { get; }

    /// Milliseconds since Unix epoch. Captured at Apply time, immutable.
    long OccurredAt { get; }
}

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

public sealed record StoredEvent(
    string StreamId,
    long Version,
    string EventType,
    string PayloadJson,
    long OccurredAt);

public sealed class ConcurrencyException(string streamId, long expected, long actual)
    : Exception($"Stream {streamId} expected version {expected}, was {actual}");

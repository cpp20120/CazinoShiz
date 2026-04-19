// ─────────────────────────────────────────────────────────────────────────────
// Projections — read models for event-sourced modules.
//
// An event-sourced aggregate is a great write model: consistency, invariants,
// replay. It's a terrible read model: "list all active rooms with at least
// three players" requires replaying every game's events. Projections fix
// that — a background process subscribes to the event stream and maintains
// a dedicated table optimized for queries.
//
// Flow:
//   1. Aggregate emits event → EventStoreRepository appends it to the stream.
//   2. Host-side EventDispatcher picks up the append and invokes matching
//      projection handlers, in the same transaction.
//   3. Projection updates its own table (sh_active_rooms, poker_leaderboards,
//      whatever).
//   4. Admin pages and analytics read from the projection, not the stream.
//
// Same-transaction dispatch keeps the projection and event store consistent
// without a retry queue. If the projection write fails, the event append
// rolls back too — the aggregate command looks atomic from the caller's POV.
//
// Why the module owns the projection instead of the Host:
//   the shape of the read model is domain knowledge. "Active room" means
//   different things in Poker vs SecretHitler; only the module knows what
//   columns the admin UI actually wants.
// ─────────────────────────────────────────────────────────────────────────────

namespace BotFramework.Sdk;

/// A projection reacts to one or more event types by mutating its own state
/// store. The framework invokes Apply once per event, in stream order, within
/// the same transaction as the event-store append.
public interface IProjection
{
    /// The event types this projection cares about. Host uses the list to skip
    /// projections that don't subscribe to a given event, so a no-op projection
    /// doesn't get called for every unrelated event in the store.
    IReadOnlySet<string> SubscribedEventTypes { get; }

    Task ApplyAsync(IDomainEvent ev, ProjectionContext ctx, CancellationToken ct);
}

/// Context passed by the Host on each Apply — carries the DB transaction the
/// event-store append is already running inside, plus enough metadata that
/// projections can order/dedupe correctly when rebuilt from history.
public sealed record ProjectionContext(
    string StreamId,
    long StreamVersion,
    long OccurredAt,
    object Transaction);

/// Marker for a projection that can rebuild itself from scratch. Host exposes
/// an admin command ("rebuild sh_active_rooms") that truncates the target
/// table and replays the whole event store. Useful when the projection shape
/// changes across deploys.
public interface IRebuildableProjection : IProjection
{
    Task ResetAsync(CancellationToken ct);
}

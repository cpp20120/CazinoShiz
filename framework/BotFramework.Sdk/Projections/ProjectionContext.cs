// ─────────────────────────────────────────────────────────────────────────────
// Projections — read models for event-sourced modules.
//
// An event-sourced aggregate is a great write model: consistency, invariants,
// replay. It's a terrible read model: "list all active rooms with at least
// three players" requires replaying every game's events. Projections fix
// that — they subscribe to the event stream and maintain dedicated tables
// optimized for queries.
//
// Flow:
//   1. Aggregate emits event → EventSourcedRepository appends it through
//      IEventStore.
//   2. EventSourcedRepository asks the Host-side EventDispatcher to invoke
//      matching projections, cross-module subscribers and analytics mirrors.
//   3. Projection updates its own table (sh_active_rooms, poker_leaderboards,
//      whatever).
//   4. Admin pages and analytics read from the projection, not the stream.
//
// The current Host implementation dispatches projections after the event-store
// append commits. Projection handlers must therefore be idempotent and rebuilds
// should be used as recovery if a projection write fails. ProjectionContext can
// carry a provider-specific transaction object for future same-transaction
// unit-of-work implementations; today it is normally null.
//
// Why the module owns the projection instead of the Host:
//   the shape of the read model is domain knowledge. "Active room" means
//   different things in Poker vs SecretHitler; only the module knows what
//   columns the admin UI actually wants.
// ─────────────────────────────────────────────────────────────────────────────

namespace BotFramework.Sdk;
/// Context passed by the Host on each Apply — carries stream metadata plus an
/// optional provider-specific transaction object. Transaction is null in the
/// current post-commit dispatch mode.
public sealed record ProjectionContext(
    string StreamId,
    long StreamVersion,
    long OccurredAt,
    object? Transaction);

// ─────────────────────────────────────────────────────────────────────────────
// Cross-module domain event bus.
//
// Event-sourced modules already publish domain events into module_events.
// The EventDispatcher fans them into projections and analytics. The missing
// piece: letting OTHER modules react to them without direct coupling.
//
// Example the bus unlocks:
//   • LeaderboardModule subscribes to "sh.game_ended" and "poker.hand_won"
//     from all modules, maintains a cross-game leaderboard projection
//   • AchievementsModule subscribes to "*.game_ended" and hands out XP
//   • TelegramNotifierModule subscribes to "*.player_joined" and bumps the
//     shared stats channel
//
// None of those subscribers import Poker or SH types; they subscribe by
// event-type string ("sh.*" patterns). Cross-module coupling through the
// shared vocabulary of event names, not through C# type references.
//
// Delivery semantics — IN-PROCESS, SYNCHRONOUS, SAME-TRANSACTION:
//   Subscribers run in the same transaction as the event-store append, just
//   like projections. If a subscriber throws, the append rolls back. This is
//   the right default for a monolith; for multi-process cross-module fan-out
//   you plug in a real broker behind the same IDomainEventBus interface and
//   accept at-least-once, eventually-consistent semantics.
//
// Why not Kafka from day one:
//   zero modules need it today. Keeping delivery in-process preserves the
//   "single transaction or bust" story. Adding Kafka later is an additive
//   change — swap IDomainEventBus implementation, modules are unaware.
// ─────────────────────────────────────────────────────────────────────────────

namespace BotFramework.Sdk;

public interface IDomainEventBus
{
    /// Called by the framework after the event-store append. Not called by
    /// modules directly — modules mutate their aggregate and the
    /// EventStoreRepository drives publish. Exposed here for the rare case
    /// of synthesizing "integration events" that don't correspond to a
    /// stored domain event.
    Task PublishAsync(IDomainEvent ev, CancellationToken ct);

    /// Subscribe a handler to events matching the pattern. Patterns use the
    /// module-id prefix convention: "sh.game_ended" (exact), "sh.*" (module
    /// wildcard), "*.game_ended" (action wildcard), "*" (all).
    void Subscribe(string eventTypePattern, IDomainEventSubscriber subscriber);
}

public interface IDomainEventSubscriber
{
    /// Runs inside the event-store append transaction. MUST be fast; any
    /// work that could be slow goes to a background job triggered by the
    /// subscriber.
    Task HandleAsync(IDomainEvent ev, CancellationToken ct);
}

/// Convenience base for type-safe subscribers that only care about one event
/// shape. Module authors typically inherit from this rather than implementing
/// the raw interface.
public abstract class DomainEventSubscriber<TEvent> : IDomainEventSubscriber where TEvent : IDomainEvent
{
    public Task HandleAsync(IDomainEvent ev, CancellationToken ct) =>
        ev is TEvent typed ? HandleAsync(typed, ct) : Task.CompletedTask;

    protected abstract Task HandleAsync(TEvent ev, CancellationToken ct);
}

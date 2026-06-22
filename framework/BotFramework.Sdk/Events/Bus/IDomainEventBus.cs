// ─────────────────────────────────────────────────────────────────────────────
// Cross-module domain event bus.
//
// Event-sourced modules publish domain events through EventSourcedRepository.
// After the event-store append commits, EventDispatcher fans the event into
// projections, analytics and this bus. The bus lets OTHER modules react to
// domain events without directly referencing the publishing module.
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
// Delivery semantics:
//   The default in-process bus is synchronous and sequential. CAP-backed
//   delivery publishes an envelope for cross-pod fan-out when Redis/CAP is
//   enabled. Subscriber failures are isolated by the Host implementations and
//   should be handled with idempotent subscribers plus replay/rebuild where
//   appropriate.
// ─────────────────────────────────────────────────────────────────────────────

namespace BotFramework.Sdk;
public interface IDomainEventBus
{
    /// Called by the framework after the event-store append. Not called by
    /// modules directly — modules mutate their aggregate and the
    /// EventSourcedRepository drives publish. Exposed here for the rare case
    /// of synthesizing "integration events" that don't correspond to a
    /// stored domain event.
    Task PublishAsync(IDomainEvent ev, CancellationToken ct);

    /// Subscribe a handler to events matching the pattern. Patterns use the
    /// module-id prefix convention: "sh.game_ended" (exact), "sh.*" (module
    /// wildcard), "*.game_ended" (action wildcard), "*" (all).
    void Subscribe(string eventTypePattern, IDomainEventSubscriber subscriber);
}

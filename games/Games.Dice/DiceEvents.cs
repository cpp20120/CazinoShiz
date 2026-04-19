// ─────────────────────────────────────────────────────────────────────────────
// Cross-module domain events the Dice module publishes.
//
// Stateless modules don't persist these to the event store (no aggregate, no
// stream); they publish directly through IDomainEventBus so Leaderboard,
// Achievements, and other cross-cutting subscribers can pattern-match on
// "dice.*" or "*.roll_completed" without depending on Games.Dice types.
// ─────────────────────────────────────────────────────────────────────────────

using BotFramework.Sdk;

namespace Games.Dice;

public sealed record DiceRollCompleted(
    long UserId,
    int DiceValue,
    int Prize,
    int Loss,
    long OccurredAt) : IDomainEvent
{
    public string EventType => "dice.roll_completed";
}

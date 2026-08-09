namespace Games.Poker.Domain.Events;

/// <summary>
/// Game fact emitted after a committed poker transition. Wagering uses the
/// BetId to settle the player's reservation; no balance or stake is stored in
/// the poker aggregate.
/// </summary>
public sealed record PokerWagerOutcomeDeclared(
    string BetId,
    string PlayerId,
    string OutcomeCode,
    string Evidence,
    long OccurredAt) : IDomainEvent
{
    public string EventType => "poker.wager.outcome_declared";
}

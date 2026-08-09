namespace Games.Blackjack.Domain.Events;

/// <summary>
/// Transactional bridge event. It contains only game facts; the integration
/// event consumed by Wagering is produced by a post-commit relay.
/// </summary>
public sealed record BlackjackWagerOutcomeDeclared(
    string BetId,
    string PlayerId,
    string OutcomeCode,
    string Evidence,
    long Revision,
    long OccurredAt) : IDomainEvent
{
    public string EventType => "blackjack.wager.outcome_declared";
}

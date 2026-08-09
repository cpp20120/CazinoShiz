namespace Games.Horse.Domain.Events;

/// <summary>Committed race result for one wager; Wagering performs the money settlement.</summary>
public sealed record HorseWagerOutcomeDeclared(
    string BetId,
    string PlayerId,
    string OutcomeCode,
    string Evidence,
    long OccurredAt) : IDomainEvent
{
    public string EventType => "horse.wager.outcome_declared";
}

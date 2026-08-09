namespace Games.SecretHitler.Domain.Events;

public sealed record SecretHitlerWagerOutcomeDeclared(
    string BetId,
    string PlayerId,
    string OutcomeCode,
    string Evidence,
    long OccurredAt) : IDomainEvent
{
    public string EventType => "sh.wager.outcome_declared";
}

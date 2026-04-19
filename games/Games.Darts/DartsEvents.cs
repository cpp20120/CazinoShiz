using BotFramework.Sdk;

namespace Games.Darts;

public sealed record DartsThrowCompleted(
    long UserId,
    long ChatId,
    int Face,
    int Bet,
    int Multiplier,
    int Payout,
    long OccurredAt) : IDomainEvent
{
    public string EventType => "darts.throw_completed";
}

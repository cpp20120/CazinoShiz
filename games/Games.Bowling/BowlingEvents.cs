using BotFramework.Sdk;

namespace Games.Bowling;

public sealed record BowlingRollCompleted(
    long UserId,
    long ChatId,
    int Face,
    int Bet,
    int Multiplier,
    int Payout,
    long OccurredAt) : IDomainEvent
{
    public string EventType => "bowling.roll_completed";
}

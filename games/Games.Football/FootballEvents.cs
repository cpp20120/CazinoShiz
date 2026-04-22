using BotFramework.Sdk;

namespace Games.Football;

public sealed record FootballThrowCompleted(
    long UserId,
    long ChatId,
    int Face,
    int Bet,
    int Multiplier,
    int Payout,
    long OccurredAt) : IDomainEvent
{
    public string EventType => "football.throw_completed";
}

using BotFramework.Sdk;

namespace Games.Basketball;

public sealed record BasketballThrowCompleted(
    long UserId,
    long ChatId,
    int Face,
    int Bet,
    int Multiplier,
    int Payout,
    long OccurredAt) : IDomainEvent
{
    public string EventType => "basketball.throw_completed";
}

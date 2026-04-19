using BotFramework.Sdk;

namespace Games.DiceCube;

public sealed record DiceCubeRollCompleted(
    long UserId,
    long ChatId,
    int Face,
    int Bet,
    int Multiplier,
    int Payout,
    long OccurredAt) : IDomainEvent
{
    public string EventType => "dicecube.roll_completed";
}

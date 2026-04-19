using BotFramework.Sdk;

namespace Games.Horse;

public sealed record HorseRaceFinished(
    string RaceDate,
    int Winner,
    int BetsCount,
    int PayoutCount,
    int Pot,
    long OccurredAt) : IDomainEvent
{
    public string EventType => "horse.race_finished";
}

public sealed record HorseBetPlaced(
    long UserId,
    int HorseId,
    int Amount,
    string RaceDate,
    long OccurredAt) : IDomainEvent
{
    public string EventType => "horse.bet_placed";
}

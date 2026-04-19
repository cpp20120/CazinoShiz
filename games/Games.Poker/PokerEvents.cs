using BotFramework.Sdk;

namespace Games.Poker;

public sealed record PokerTableCreated(string InviteCode, long HostUserId, int BuyIn, long OccurredAt) : IDomainEvent
{
    public string EventType => "poker.table_created";
}

public sealed record PokerPlayerJoined(string InviteCode, long UserId, int Position, int BuyIn, long OccurredAt) : IDomainEvent
{
    public string EventType => "poker.player_joined";
}

public sealed record PokerHandStarted(string InviteCode, int Players, long OccurredAt) : IDomainEvent
{
    public string EventType => "poker.hand_started";
}

public sealed record PokerHandEnded(
    string InviteCode,
    string Reason,
    IReadOnlyList<(long UserId, int Amount)> Winners,
    long OccurredAt) : IDomainEvent
{
    public string EventType => "poker.hand_ended";
}

using BotFramework.Sdk;

namespace Games.SecretHitler;

public sealed record SecretHitlerGameCreated(string InviteCode, long HostUserId, int BuyIn, long OccurredAt) : IDomainEvent
{
    public string EventType => "sh.game_created";
}

public sealed record SecretHitlerPlayerJoined(string InviteCode, long UserId, int Position, int BuyIn, long OccurredAt) : IDomainEvent
{
    public string EventType => "sh.player_joined";
}

public sealed record SecretHitlerGameStarted(string InviteCode, int Players, long OccurredAt) : IDomainEvent
{
    public string EventType => "sh.game_started";
}

public sealed record SecretHitlerGameEnded(
    string InviteCode,
    ShWinner Winner,
    ShWinReason Reason,
    IReadOnlyList<(long UserId, int Amount)> Payouts,
    long OccurredAt) : IDomainEvent
{
    public string EventType => "sh.game_ended";
}

using BotFramework.Sdk;

namespace Games.Redeem;

public sealed record RedeemCodeIssued(Guid Code, long IssuedBy, long OccurredAt) : IDomainEvent
{
    public string EventType => "redeem.issued";
}

public sealed record RedeemCodeRedeemed(Guid Code, long IssuedBy, long RedeemedBy, int CoinReward, long OccurredAt) : IDomainEvent
{
    public string EventType => "redeem.redeemed";
}

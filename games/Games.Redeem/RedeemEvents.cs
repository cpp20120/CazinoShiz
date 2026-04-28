using BotFramework.Sdk;

namespace Games.Redeem;

public sealed record RedeemCodeIssued(Guid Code, long IssuedBy, string FreeSpinGameId, long OccurredAt) : IDomainEvent
{
    public string EventType => "redeem.issued";
}

public sealed record RedeemCodeRedeemed(Guid Code, long IssuedBy, long RedeemedBy, string FreeSpinGameId, long OccurredAt) : IDomainEvent
{
    public string EventType => "redeem.redeemed";
}

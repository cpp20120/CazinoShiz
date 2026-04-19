namespace Games.Redeem;

public sealed class RedeemCode
{
    public Guid Code { get; set; }
    public bool Active { get; set; } = true;
    public long IssuedBy { get; set; }
    public long IssuedAt { get; set; }
    public long? RedeemedBy { get; set; }
    public long? RedeemedAt { get; set; }
}

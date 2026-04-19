namespace Games.Redeem;

public sealed class RedeemOptions
{
    public const string SectionName = "Games:redeem";

    public int CoinReward { get; init; } = 50;
    public int CaptchaItems { get; init; } = 6;
    public int CaptchaTimeoutMs { get; init; } = 15_000;
    public List<long> Admins { get; init; } = [];
}

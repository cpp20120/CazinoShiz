using BotFramework.Sdk;

namespace Games.Redeem;

public sealed class RedeemOptions
{
    public const string SectionName = "Games:redeem";

    public string FreeSpinGameId { get; init; } = MiniGameIds.Dice;
    public int CaptchaItems { get; init; } = 6;
    public int CaptchaTimeoutMs { get; init; } = 15_000;
    public int MaxCodegenCount { get; init; } = 20;
    public List<long> Admins { get; init; } = [];
}

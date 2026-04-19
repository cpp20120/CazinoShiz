using BotFramework.Host.Services;

namespace Games.Redeem;

public enum RedeemError
{
    None = 0,
    InvalidCode,
    AlreadyRedeemed,
    SelfRedeem,
    NoUser,
}

public sealed record BeginRedeemResult(
    RedeemError Error,
    Guid CodeGuid = default,
    CaptchaResult? Captcha = null);

public sealed record CompleteRedeemResult(
    RedeemError Error,
    int CoinReward = 0);

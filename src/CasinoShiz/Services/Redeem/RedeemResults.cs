namespace CasinoShiz.Services.Redeem;

public enum BeginRedeemError
{
    None = 0,
    InvalidCode,
    AlreadyRedeemed,
    SelfRedeem,
    NoUser,
}

public sealed record BeginRedeemResult(
    BeginRedeemError Error,
    Guid CodeGuid = default,
    CaptchaResult? Captcha = null);

public enum CompleteRedeemError
{
    None = 0,
    AlreadyRedeemed,
    NoUser,
}

public sealed record CompleteRedeemResult(
    CompleteRedeemError Error,
    long? IssuedChatId = null,
    int? IssuedMessageId = null);

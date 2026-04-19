using BotFramework.Host;
using BotFramework.Host.Services;
using BotFramework.Sdk;
using Microsoft.Extensions.Options;

namespace Games.Redeem;

public interface IRedeemService
{
    Task<Guid> IssueAdminCodeAsync(long userId, CancellationToken ct);
    Task<BeginRedeemResult> BeginRedeemAsync(long userId, string displayName, string codeText, CancellationToken ct);
    Task<CompleteRedeemResult> CompleteRedeemAsync(long userId, Guid codeGuid, CancellationToken ct);
    void ReportCaptcha(long userId, string codeText, string pattern, bool passed);
}

public sealed partial class RedeemService(
    IRedeemStore store,
    IEconomicsService economics,
    IAnalyticsService analytics,
    IDomainEventBus events,
    IOptions<RedeemOptions> options,
    ILogger<RedeemService> logger) : IRedeemService
{
    private readonly RedeemOptions _opts = options.Value;

    public async Task<Guid> IssueAdminCodeAsync(long userId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var code = new RedeemCode
        {
            Code = Guid.NewGuid(),
            Active = true,
            IssuedBy = userId,
            IssuedAt = now,
        };
        await store.InsertAsync(code, ct);

        analytics.Track("redeem", "issued", new Dictionary<string, object?>
        {
            ["user_id"] = userId,
            ["code"] = code.Code.ToString(),
        });

        await events.PublishAsync(new RedeemCodeIssued(code.Code, userId, now), ct);
        LogIssued(userId, code.Code);
        return code.Code;
    }

    public async Task<BeginRedeemResult> BeginRedeemAsync(long userId, string displayName, string codeText, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(codeText) || !Guid.TryParse(codeText, out var codeGuid))
        {
            analytics.Track("redeem", "invalid_code", new Dictionary<string, object?>
            {
                ["user_id"] = userId,
                ["code_text"] = codeText,
            });
            return new BeginRedeemResult(RedeemError.InvalidCode);
        }

        var code = await store.FindAsync(codeGuid, ct);
        if (code == null || !code.Active)
        {
            analytics.Track("redeem", "already_redeemed", new Dictionary<string, object?>
            {
                ["user_id"] = userId,
                ["code"] = codeGuid.ToString(),
            });
            return new BeginRedeemResult(RedeemError.AlreadyRedeemed);
        }

        if (code.IssuedBy == userId)
        {
            analytics.Track("redeem", "self_redeem", new Dictionary<string, object?>
            {
                ["user_id"] = userId,
                ["code"] = codeGuid.ToString(),
            });
            return new BeginRedeemResult(RedeemError.SelfRedeem);
        }

        await economics.EnsureUserAsync(userId, displayName, ct);

        var captcha = CaptchaService.CreateCaptcha(codeText, _opts.CaptchaItems);
        return new BeginRedeemResult(RedeemError.None, codeGuid, captcha);
    }

    public async Task<CompleteRedeemResult> CompleteRedeemAsync(long userId, Guid codeGuid, CancellationToken ct)
    {
        var code = await store.FindAsync(codeGuid, ct);
        if (code == null || !code.Active)
            return new CompleteRedeemResult(RedeemError.AlreadyRedeemed);

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var claimed = await store.MarkRedeemedAsync(codeGuid, userId, now, ct);
        if (!claimed) return new CompleteRedeemResult(RedeemError.AlreadyRedeemed);

        await economics.CreditAsync(userId, _opts.CoinReward, "redeem", ct);

        analytics.Track("redeem", "success", new Dictionary<string, object?>
        {
            ["user_id"] = userId,
            ["code"] = codeGuid.ToString(),
            ["reward"] = _opts.CoinReward,
            ["redeem_interval_ms"] = now - code.IssuedAt,
        });

        await events.PublishAsync(new RedeemCodeRedeemed(codeGuid, code.IssuedBy, userId, _opts.CoinReward, now), ct);
        LogRedeemed(userId, codeGuid, _opts.CoinReward);
        return new CompleteRedeemResult(RedeemError.None, _opts.CoinReward);
    }

    public void ReportCaptcha(long userId, string codeText, string pattern, bool passed)
    {
        analytics.Track("redeem", passed ? "captcha_succeed" : "captcha_failed", new Dictionary<string, object?>
        {
            ["user_id"] = userId,
            ["code_text"] = codeText,
            ["pattern"] = pattern,
        });
    }

    [LoggerMessage(LogLevel.Information, "redeem.issued user={UserId} code={Code}")]
    partial void LogIssued(long userId, Guid code);

    [LoggerMessage(LogLevel.Information, "redeem.redeemed user={UserId} code={Code} reward={Reward}")]
    partial void LogRedeemed(long userId, Guid code, int reward);
}

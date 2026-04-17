using CasinoShiz.Configuration;
using CasinoShiz.Data;
using CasinoShiz.Data.Entities;
using CasinoShiz.Helpers;
using CasinoShiz.Services.Analytics;
using Microsoft.Extensions.Options;

namespace CasinoShiz.Services.Redeem;

public sealed class RedeemService(
    AppDbContext db,
    IOptions<BotOptions> options,
    ClickHouseReporter reporter,
    CaptchaService captcha)
{
    private readonly BotOptions _opts = options.Value;

    public async Task<Guid> IssueAdminCodeAsync(long userId, long chatId, CancellationToken ct)
    {
        var code = new FreespinCode
        {
            Code = Guid.NewGuid(),
            Active = true,
            IssuedBy = 0,
            IssuedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };
        db.FreespinCodes.Add(code);
        await db.SaveChangesAsync(ct);

        reporter.SendEvent(new EventData
        {
            EventType = "codegen",
            Payload = new { type = "command", chat_id = chatId, user_id = userId, code_text = code.Code.ToString(), issued_at = code.IssuedAt }
        });

        return code.Code;
    }

    public async Task<BeginRedeemResult> BeginRedeemAsync(long userId, long chatId, string codeText, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(codeText) || !Guid.TryParse(codeText, out var codeGuid))
        {
            reporter.SendEvent(new EventData
            {
                EventType = "redeem",
                Payload = new { type = "invalid", chat_id = chatId, user_id = userId, code_text = codeText }
            });
            return new BeginRedeemResult(BeginRedeemError.InvalidCode);
        }

        var code = await db.FreespinCodes.FindAsync([codeGuid], ct);
        if (code == null || !code.Active)
        {
            reporter.SendEvent(new EventData
            {
                EventType = "redeem",
                Payload = new { type = "already_redeemed", chat_id = chatId, user_id = userId, code_text = codeText }
            });
            return new BeginRedeemResult(BeginRedeemError.AlreadyRedeemed);
        }

        if (code.IssuedBy == userId)
        {
            reporter.SendEvent(new EventData
            {
                EventType = "redeem",
                Payload = new { type = "self_redeem", chat_id = chatId, user_id = userId, code_text = codeText }
            });
            return new BeginRedeemResult(BeginRedeemError.SelfRedeem);
        }

        var user = await db.Users.FindAsync([userId], ct);
        if (user == null) return new BeginRedeemResult(BeginRedeemError.NoUser);

        var result = CaptchaService.CreateCaptcha(codeText, _opts.CaptchaItems);
        return new BeginRedeemResult(BeginRedeemError.None, codeGuid, result);
    }

    public async Task<CompleteRedeemResult> CompleteRedeemAsync(long userId, Guid codeGuid, string codeText, string pattern, long chatId, CancellationToken ct)
    {
        var code = await db.FreespinCodes.FindAsync([codeGuid], ct);
        if (code == null || !code.Active)
            return new CompleteRedeemResult(CompleteRedeemError.AlreadyRedeemed);

        var user = await db.Users.FindAsync([userId], ct);
        if (user == null) return new CompleteRedeemResult(CompleteRedeemError.NoUser);

        var currentDayMs = TimeHelper.GetCurrentDayMillis();
        var isCurrentDay = currentDayMs == user.LastDayUtc;

        user.ExtraAttempts = isCurrentDay ? user.ExtraAttempts + 1 : 1;
        user.LastDayUtc = isCurrentDay ? user.LastDayUtc : currentDayMs;
        user.AttemptCount = isCurrentDay ? user.AttemptCount : 0;
        code.Active = false;
        await db.SaveChangesAsync(ct);

        reporter.SendEvent(new EventData
        {
            EventType = "redeem",
            Payload = new
            {
                type = "success", chat_id = chatId, user_id = userId, code_text = codeText,
                redeem_interval = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - code.IssuedAt,
            }
        });

        return new CompleteRedeemResult(CompleteRedeemError.None, code.ChatId, code.MessageId);
    }

    public void ReportCaptcha(long userId, long chatId, string codeText, string pattern, bool passed)
    {
        reporter.SendEvent(new EventData
        {
            EventType = "redeem",
            Payload = new
            {
                type = passed ? "captcha_succeed" : "captcha_failed",
                chat_id = chatId, user_id = userId, code_text = codeText, captcha_pattern = pattern,
            }
        });
    }
}

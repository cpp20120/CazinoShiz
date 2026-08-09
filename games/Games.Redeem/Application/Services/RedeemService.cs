using BotFramework.Host.Execution;
using Games.Redeem.Application.Execution;
using Microsoft.Extensions.Options;

namespace Games.Redeem.Application.Services;

public sealed partial class RedeemService(
    IRedeemStore store,
    IGameStateExecutor<RedeemIssueCommand, RedeemExecutionState, Guid> issueExecutor,
    IAtomicGameExecutor<RedeemCompleteCommand, RedeemExecutionState, CompleteRedeemResult> completeExecutor,
    IOptions<RedeemOptions> options,
    ILogger<RedeemService> logger) : IRedeemService
{
    private readonly RedeemOptions redeemOptions = options.Value;

    public async Task<Guid> IssueAdminCodeAsync(
        long userId, CancellationToken ct, string? freeSpinGameId = null)
    {
        var code = Guid.NewGuid();
        var gameId = string.IsNullOrWhiteSpace(freeSpinGameId)
            ? redeemOptions.FreeSpinGameId : freeSpinGameId;
        var result = await issueExecutor.ExecuteAsync(new(new RedeemIssueCommand(
            code, userId, gameId, $"redeem:issue:{code:N}")), ct);
        LogIssued(userId, result);
        return result;
    }

    public async Task<BeginRedeemResult> BeginRedeemAsync(
        long userId, long balanceScopeId, string displayName, string codeText, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(codeText) || !Guid.TryParse(codeText, out var codeGuid))
            return new(RedeemError.InvalidCode);
        var code = await store.FindAsync(codeGuid, ct);
        if (code?.Active != true) return new(RedeemError.AlreadyRedeemed);
        if (code.IssuedBy == userId) return new(RedeemError.SelfRedeem);
        return new(RedeemError.None, codeGuid,
            CaptchaService.CreateCaptcha(codeText, redeemOptions.CaptchaItems));
    }

    public async Task<CompleteRedeemResult> CompleteRedeemAsync(
        long userId, long balanceScopeId, Guid codeGuid, CancellationToken ct)
    {
        var code = await store.FindAsync(codeGuid, ct);
        if (code?.Active != true) return new(RedeemError.AlreadyRedeemed);
        var result = await completeExecutor.ExecuteAsync(new(new RedeemCompleteCommand(
            codeGuid, userId, balanceScopeId, code.FreeSpinGameId,
            $"redeem:complete:{codeGuid:N}:{userId}")), ct);
        if (result.Error == RedeemError.None)
            LogRedeemed(userId, codeGuid, result.FreeSpinGameId);
        return result;
    }

    public void ReportCaptcha(long userId, string codeText, string pattern, bool passed)
    {
        // Captcha telemetry is intentionally outside the mutation path.
    }

    [LoggerMessage(LogLevel.Information, "redeem.issued user={UserId} code={Code}")]
    partial void LogIssued(long userId, Guid code);

    [LoggerMessage(LogLevel.Information, "redeem.redeemed user={UserId} code={Code} free_spin_game_id={FreeSpinGameId}")]
    partial void LogRedeemed(long userId, Guid code, string freeSpinGameId);
}

namespace BotFramework.Sdk.Execution.DeferredOutcome;

public sealed record DeferredOutcomeWagerResolveResult<TOutcome>(
    DeferredOutcomeWagerResolveStatus Status,
    TOutcome Outcome,
    long Stake,
    long Payout,
    long Balance,
    DeferredOutcomeWagerQuotaUsage? Quota = null)
{
    public bool Resolved => Status == DeferredOutcomeWagerResolveStatus.Resolved;
}

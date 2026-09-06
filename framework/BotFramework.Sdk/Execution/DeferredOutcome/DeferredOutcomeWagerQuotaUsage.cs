namespace BotFramework.Sdk.Execution.DeferredOutcome;

public sealed record DeferredOutcomeWagerQuotaUsage(long Used, long Limit)
{
    public long Remaining => Math.Max(0, Limit - Used);
}

namespace BotFramework.Sdk.Execution.InstantWager;

public sealed record InstantWagerQuotaUsage(long Used, long Limit)
{
    public long Remaining => Math.Max(0, Limit - Used);
}

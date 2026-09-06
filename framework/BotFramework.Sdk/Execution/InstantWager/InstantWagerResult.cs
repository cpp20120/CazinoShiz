namespace BotFramework.Sdk.Execution.InstantWager;

/// <summary>Outcome of a one-shot wager, including its post-decision wallet snapshot.</summary>
public sealed record InstantWagerResult<TOutcome>(
    InstantWagerStatus Status,
    TOutcome Outcome,
    long Stake,
    long Payout,
    long Balance,
    InstantWagerQuotaUsage? Quota = null)
{
    public bool Accepted => Status == InstantWagerStatus.Accepted;
}

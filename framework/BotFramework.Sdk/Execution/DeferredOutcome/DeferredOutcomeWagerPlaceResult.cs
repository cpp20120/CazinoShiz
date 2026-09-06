namespace BotFramework.Sdk.Execution.DeferredOutcome;

public sealed record DeferredOutcomeWagerPlaceResult(
    DeferredOutcomeWagerPlaceStatus Status,
    long Balance,
    long? PendingAmount = null,
    string? BlockingGameId = null,
    DeferredOutcomeWagerQuotaUsage? Quota = null)
{
    public bool Accepted => Status == DeferredOutcomeWagerPlaceStatus.Accepted;
}

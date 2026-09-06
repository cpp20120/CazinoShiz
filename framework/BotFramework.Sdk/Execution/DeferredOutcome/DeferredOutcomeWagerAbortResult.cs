namespace BotFramework.Sdk.Execution.DeferredOutcome;

public sealed record DeferredOutcomeWagerAbortResult(DeferredOutcomeWagerAbortStatus Status)
{
    public bool Aborted => Status == DeferredOutcomeWagerAbortStatus.Aborted;
}

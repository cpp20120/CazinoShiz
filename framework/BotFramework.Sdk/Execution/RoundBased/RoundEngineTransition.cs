namespace BotFramework.Sdk.Execution;

/// <summary>Result of an immutable round-engine state transition.</summary>
public sealed record RoundEngineTransition<TPhase>(
    RoundEngineTransitionStatus Status,
    RoundEngineState<TPhase> State,
    RoundEngineRejection? Rejection = null)
    where TPhase : notnull
{
    public bool Applied => Status == RoundEngineTransitionStatus.Applied;
}

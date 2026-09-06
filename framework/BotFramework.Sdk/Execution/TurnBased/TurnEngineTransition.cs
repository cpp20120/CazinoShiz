namespace BotFramework.Sdk.Execution;

/// <summary>Result of an immutable turn-engine state transition.</summary>
public sealed record TurnEngineTransition<TPlayerId>(
    TurnEngineTransitionStatus Status,
    TurnEngineState<TPlayerId> State,
    TurnEngineRejection? Rejection = null)
    where TPlayerId : notnull
{
    public bool Applied => Status == TurnEngineTransitionStatus.Applied;
}

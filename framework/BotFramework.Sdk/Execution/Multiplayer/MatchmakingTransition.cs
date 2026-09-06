namespace BotFramework.Sdk.Execution;

/// <summary>Result of enqueue, dequeue or match formation.</summary>
public sealed record MatchmakingTransition<TPlayerId, TMatchKey>(
    MatchmakingTransitionStatus Status,
    MatchmakingState<TPlayerId, TMatchKey> State,
    MatchmakingMatch<TPlayerId, TMatchKey>? Match = null,
    MatchmakingRejection? Rejection = null)
    where TPlayerId : notnull
    where TMatchKey : notnull
{
    public bool Applied => Status == MatchmakingTransitionStatus.Applied;
}

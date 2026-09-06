namespace BotFramework.Sdk.Execution;

/// <summary>Result of an immutable lobby transition.</summary>
public sealed record LobbyTransition<TPlayerId>(
    LobbyTransitionStatus Status,
    LobbyState<TPlayerId> State,
    LobbyRejection? Rejection = null)
    where TPlayerId : notnull
{
    public bool Applied => Status == LobbyTransitionStatus.Applied;
}

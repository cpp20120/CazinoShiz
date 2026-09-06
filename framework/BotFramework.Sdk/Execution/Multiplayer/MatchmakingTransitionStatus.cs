namespace BotFramework.Sdk.Execution;

/// <summary>Outcome of an immutable matchmaking queue operation.</summary>
public enum MatchmakingTransitionStatus
{
    Applied,
    Rejected,
    NoMatch,
}

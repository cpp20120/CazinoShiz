namespace BotFramework.Sdk.Execution;

/// <summary>Stable reasons for a generic turn-engine transition rejection.</summary>
public enum TurnEngineRejectionReason
{
    GameNotWaiting,
    GameNotActive,
    GameNotSuspended,
    GameIsTerminal,
    NoPlayers,
    PlayerAlreadyJoined,
    PlayerNotFound,
    StaleTurn,
    DeadlineNotSet,
    TurnNotExpired,
}

namespace BotFramework.Sdk.Execution;

/// <summary>Common rejections that can be shared across unrelated game rules.</summary>
public enum GameRuleRejectionKind
{
    Forbidden,
    GameNotActive,
    PlayerNotJoined,
    NotYourTurn,
    InsufficientPlayers,
    LobbyNotReady,
    PhaseClosed,
    DeadlineExpired,
    StaleInput,
    PermissionDenied,
    DuplicateAction,
    InvalidInput,
    Custom,
}

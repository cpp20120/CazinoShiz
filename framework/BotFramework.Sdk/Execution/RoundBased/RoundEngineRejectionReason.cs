namespace BotFramework.Sdk.Execution;

/// <summary>Stable reasons for a generic round-engine transition rejection.</summary>
public enum RoundEngineRejectionReason
{
    GameNotWaiting,
    GameNotActive,
    GameNotSuspended,
    GameIsTerminal,
    StalePhase,
    DeadlineNotSet,
    PhaseNotExpired,
}

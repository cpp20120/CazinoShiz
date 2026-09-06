namespace BotFramework.Sdk.Execution;

/// <summary>Lifecycle of a game driven by explicit phases and rounds.</summary>
public enum RoundGameStatus
{
    WaitingToStart,
    Active,
    Suspended,
    Completed,
    Aborted,
}

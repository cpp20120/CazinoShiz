namespace BotFramework.Sdk.Execution;

/// <summary>Framework-owned progression selected by a pure phase rule.</summary>
public enum RoundCompletionKind
{
    Continue,
    Complete,
    AdvancePhase,
    StartNextRound,
    WaitForInput,
}

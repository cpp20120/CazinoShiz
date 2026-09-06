namespace BotFramework.Sdk.Execution;

/// <summary>Framework-owned instruction for what happens after a turn rule succeeds.</summary>
public enum TurnCompletionKind
{
    Continue,
    Complete,
    PassTo,
    WaitForInput,
}

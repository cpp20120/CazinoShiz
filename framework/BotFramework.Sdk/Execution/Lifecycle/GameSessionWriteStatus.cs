namespace BotFramework.Sdk.Execution.Lifecycle;

public enum GameSessionWriteStatus
{
    Applied,
    AlreadyApplied,
    RevisionConflict,
    SessionAlreadyExists,
}

namespace BotFramework.Sdk.Execution.Lifecycle;

/// <summary>Durable, transport-neutral lifecycle states for a game flow.</summary>
public enum GameSessionLifecycle
{
    Started,
    Suspended,
    Resumed,
    Completed,
    Failed,
}

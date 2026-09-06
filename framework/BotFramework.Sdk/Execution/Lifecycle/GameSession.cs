namespace BotFramework.Sdk.Execution.Lifecycle;

/// <summary>
/// Snapshot of a durable game flow. Owner and scope are opaque identifiers so
/// adapters can map Telegram, Discord, REST or another transport without
/// exposing their types to a game model.
/// </summary>
public sealed record GameSession(
    string SessionId,
    string GameId,
    string OwnerId,
    string ScopeId,
    string RootCorrelationId,
    string LastCorrelationId,
    GameSessionLifecycle Lifecycle,
    long Revision,
    string Data,
    string? FailureCode,
    DateTimeOffset StartedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ExpiresAt)
{
    public bool IsTerminal => Lifecycle is GameSessionLifecycle.Completed or GameSessionLifecycle.Failed;

    public bool IsActive => Lifecycle is GameSessionLifecycle.Started or GameSessionLifecycle.Resumed;
}

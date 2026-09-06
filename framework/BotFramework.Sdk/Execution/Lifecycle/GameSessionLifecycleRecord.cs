namespace BotFramework.Sdk.Execution.Lifecycle;

/// <summary>Immutable audit record for one lifecycle operation.</summary>
public sealed record GameSessionLifecycleRecord(
    string SessionId,
    string CorrelationId,
    GameSessionLifecycle Lifecycle,
    long Revision,
    string? FailureCode,
    DateTimeOffset OccurredAt);

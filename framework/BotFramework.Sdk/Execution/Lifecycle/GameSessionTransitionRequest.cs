namespace BotFramework.Sdk.Execution.Lifecycle;

/// <summary>
/// Inputs for a lifecycle transition. Correlation id is the idempotency key for
/// this one transition; callers resume a flow by issuing a new correlation id.
/// </summary>
public sealed record GameSessionTransitionRequest(
    string SessionId,
    string CorrelationId,
    long ExpectedRevision,
    string? Data = null,
    DateTimeOffset? ExpiresAt = null,
    string? FailureCode = null);

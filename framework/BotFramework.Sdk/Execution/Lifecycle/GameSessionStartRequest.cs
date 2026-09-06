namespace BotFramework.Sdk.Execution.Lifecycle;

/// <summary>Inputs required to create a new durable game session.</summary>
public sealed record GameSessionStartRequest(
    string GameId,
    string OwnerId,
    string ScopeId,
    string CorrelationId,
    string? Data = null,
    DateTimeOffset? ExpiresAt = null,
    string? SessionId = null);

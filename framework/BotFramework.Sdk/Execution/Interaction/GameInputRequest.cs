namespace BotFramework.Sdk.Execution;

/// <summary>Persisted interaction route and the player it is bound to.</summary>
public sealed record GameInputRequest(
    string RequestId,
    string GameId,
    string AggregateId,
    string ExpectedPlayerId,
    string ScopeId,
    string Route,
    string Payload,
    IReadOnlyList<string> AllowedValues,
    string? SessionId,
    DateTimeOffset ExpiresAt,
    GameInputRequestStatus Status,
    string? ConsumedCorrelationId,
    string? ConsumedValue,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ConsumedAt);

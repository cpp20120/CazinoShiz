namespace BotFramework.Sdk.Execution;

/// <summary>
/// Declarative request for one interaction. It is written in the game
/// transaction, while the matching presentation effect can be delivered from a
/// durable effect outbox after commit.
/// </summary>
public sealed record InputRequestEffect : IGameCapabilityEffect
{
    public InputRequestEffect(
        string requestId,
        string expectedPlayerId,
        string scopeId,
        string route,
        DateTimeOffset expiresAt,
        string? payload = null,
        IEnumerable<string>? allowedValues = null,
        string? sessionId = null)
    {
        RequestId = GameInputRequestData.RequireId(requestId, nameof(requestId));
        ExpectedPlayerId = GameInputRequestData.RequireId(expectedPlayerId, nameof(expectedPlayerId));
        ScopeId = GameInputRequestData.RequireId(scopeId, nameof(scopeId));
        Route = GameInputRequestData.RequireId(route, nameof(route));
        ExpiresAt = expiresAt;
        Payload = GameInputRequestData.NormalizeJson(payload);
        AllowedValues = GameInputRequestData.NormalizeAllowedValues(allowedValues);
        SessionId = sessionId is null ? null : GameInputRequestData.RequireId(sessionId, nameof(sessionId));
    }

    public string RequestId { get; }

    public string ExpectedPlayerId { get; }

    public string ScopeId { get; }

    /// <summary>Module-owned route resolved after a valid input is consumed.</summary>
    public string Route { get; }

    public DateTimeOffset ExpiresAt { get; }

    public string Payload { get; }

    /// <summary>Optional whitelist of values accepted from a frontend.</summary>
    public IReadOnlyList<string> AllowedValues { get; }

    /// <summary>Optional durable flow the module may resume through its route handler.</summary>
    public string? SessionId { get; }

    public GameCapability RequiredCapability => MessagingCapabilities.Input;
}
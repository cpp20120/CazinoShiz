namespace BotFramework.Sdk.Execution;

/// <summary>Opaque, transport-independent request submitted by a frontend.</summary>
public sealed record GameInputSubmission
{
    public GameInputSubmission(
        string requestId,
        string playerId,
        string scopeId,
        string value,
        string correlationId)
    {
        RequestId = GameInputRequestData.RequireId(requestId, nameof(requestId));
        PlayerId = GameInputRequestData.RequireId(playerId, nameof(playerId));
        ScopeId = GameInputRequestData.RequireId(scopeId, nameof(scopeId));
        Value = GameInputRequestData.RequireId(value, nameof(value));
        CorrelationId = GameInputRequestData.RequireId(correlationId, nameof(correlationId));
    }

    public string RequestId { get; }

    public string PlayerId { get; }

    public string ScopeId { get; }

    public string Value { get; }

    /// <summary>Idempotency key of this input delivery and resulting game command.</summary>
    public string CorrelationId { get; }
}
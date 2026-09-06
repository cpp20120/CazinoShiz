namespace BotFramework.Narrative;

/// <summary>
/// Execution metadata exposed to an adapter without leaking Host, database or
/// transport types into a game module.
/// </summary>
public sealed record NarrativeDeliveryContext
{
    public NarrativeDeliveryContext(
        string? operationId = null,
        IReadOnlyDictionary<string, string?>? metadata = null,
        string? deliveryId = null)
    {
        OperationId = operationId;
        Metadata = NarrativeValue.CopyMetadata(metadata, nameof(metadata));
        DeliveryId = deliveryId is null ? null : NarrativeValue.Required(deliveryId, nameof(deliveryId));
    }

    public string? OperationId { get; }

    public IReadOnlyDictionary<string, string?> Metadata { get; }

    /// <summary>
    /// Stable id of one durable effect delivery. A projection uses it to make
    /// at-least-once outbox retries idempotent.
    /// </summary>
    public string? DeliveryId { get; }
}

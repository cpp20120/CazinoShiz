namespace BotFramework.Presentation;

/// <summary>
/// Execution metadata exposed to a frontend adapter without leaking Host,
/// database or transport types into a game module.
/// </summary>
public sealed record PresentationDeliveryContext
{
    public PresentationDeliveryContext(
        string? operationId = null,
        IReadOnlyDictionary<string, string?>? metadata = null,
        string? deliveryId = null)
    {
        OperationId = operationId;
        Metadata = PresentationValue.CopyMetadata(metadata, nameof(metadata));
        DeliveryId = deliveryId is null
            ? null
            : PresentationValue.Required(deliveryId, nameof(deliveryId));
    }

    public string? OperationId { get; }

    public IReadOnlyDictionary<string, string?> Metadata { get; }

    /// <summary>Stable id of one durable effect delivery for sink-side idempotency.</summary>
    public string? DeliveryId { get; }
}

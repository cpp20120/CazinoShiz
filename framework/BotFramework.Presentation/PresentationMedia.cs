namespace BotFramework.Presentation;

/// <summary>
/// Opaque reference to media owned by the application. A frontend decides how
/// to resolve, upload, cache or render this resource.
/// </summary>
public sealed record PresentationMedia
{
    public PresentationMedia(
        PresentationMediaKind kind,
        string resourceId,
        string? contentType = null,
        PresentationText? altText = null)
    {
        Kind = kind;
        ResourceId = PresentationValue.Required(resourceId, nameof(resourceId));
        ContentType = contentType is null
            ? null
            : PresentationValue.Required(contentType, nameof(contentType));
        AltText = altText;
    }

    public PresentationMediaKind Kind { get; }

    public string ResourceId { get; }

    public string? ContentType { get; }

    public PresentationText? AltText { get; }
}

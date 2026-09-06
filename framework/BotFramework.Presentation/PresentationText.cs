namespace BotFramework.Presentation;

/// <summary>
/// Localizable semantic text. A frontend resolves <see cref="Key"/> and may
/// fall back to <see cref="Fallback"/> when no catalogue entry is available.
/// </summary>
public sealed record PresentationText
{
    public PresentationText(
        string key,
        IReadOnlyDictionary<string, string?>? parameters = null,
        string? fallback = null)
    {
        Key = PresentationValue.Required(key, nameof(key));
        Parameters = PresentationValue.CopyMetadata(parameters, nameof(parameters));
        Fallback = fallback;
    }

    public string Key { get; }

    public IReadOnlyDictionary<string, string?> Parameters { get; }

    public string? Fallback { get; }
}

namespace BotFramework.Narrative;

/// <summary>
/// Localizable semantic text. Frontends resolve <see cref="Key"/> and may use
/// <see cref="Fallback"/> when no catalogue entry is available.
/// </summary>
public sealed record NarrativeText
{
    public NarrativeText(
        string key,
        IReadOnlyDictionary<string, string?>? parameters = null,
        string? fallback = null)
    {
        Key = NarrativeValue.Required(key, nameof(key));
        Parameters = NarrativeValue.CopyMetadata(parameters, nameof(parameters));
        Fallback = fallback;
    }

    public string Key { get; }

    public IReadOnlyDictionary<string, string?> Parameters { get; }

    public string? Fallback { get; }
}

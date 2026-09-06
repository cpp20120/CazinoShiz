namespace BotFramework.Narrative;

/// <summary>One semantic option in an interaction presented by <see cref="ChoiceEffect"/>.</summary>
public sealed record NarrativeChoiceOption
{
    public NarrativeChoiceOption(
        string optionId,
        NarrativeText label,
        string? value = null,
        bool enabled = true)
    {
        OptionId = NarrativeValue.Required(optionId, nameof(optionId));
        Label = label ?? throw new ArgumentNullException(nameof(label));
        Value = value;
        Enabled = enabled;
    }

    public string OptionId { get; }

    public NarrativeText Label { get; }

    /// <summary>Opaque game-owned value returned with the selected option.</summary>
    public string? Value { get; }

    public bool Enabled { get; }
}

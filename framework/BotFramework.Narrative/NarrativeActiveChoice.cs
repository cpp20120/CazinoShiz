namespace BotFramework.Narrative;

/// <summary>Current selectable narrative input for one projected recipient.</summary>
public sealed record NarrativeActiveChoice
{
    public NarrativeActiveChoice(
        string interactionId,
        IReadOnlyList<NarrativeChoiceOption> options,
        DateTimeOffset? expiresAt,
        bool allowsMultiple)
    {
        InteractionId = NarrativeValue.Required(interactionId, nameof(interactionId));
        Options = NarrativeValue.CopyRequired(options, nameof(options));
        if (Options.Select(option => option.OptionId).Distinct(StringComparer.Ordinal).Take(Options.Count + 1).Count() != Options.Count)
            throw new ArgumentException("Option ids must be unique.", nameof(options));

        ExpiresAt = expiresAt;
        AllowsMultiple = allowsMultiple;
    }

    public string InteractionId { get; }

    public IReadOnlyList<NarrativeChoiceOption> Options { get; }

    public DateTimeOffset? ExpiresAt { get; }

    public bool AllowsMultiple { get; }
}

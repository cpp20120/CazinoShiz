using BotFramework.Sdk.Execution;

namespace BotFramework.Narrative;

/// <summary>Requests a frontend to collect a semantic choice from a player.</summary>
public sealed record ChoiceEffect : NarrativeEffect
{
    public ChoiceEffect(
        NarrativeAddress target,
        string interactionId,
        IEnumerable<NarrativeChoiceOption> options,
        DateTimeOffset? expiresAt = null,
        bool allowsMultiple = false)
        : base(target)
    {
        InteractionId = NarrativeValue.Required(interactionId, nameof(interactionId));
        Options = NarrativeValue.CopyRequired(options, nameof(options));
        if (Options.Select(option => option.OptionId).Distinct(StringComparer.Ordinal).Count() != Options.Count)
            throw new ArgumentException("Option ids must be unique.", nameof(options));

        ExpiresAt = expiresAt;
        AllowsMultiple = allowsMultiple;
    }

    public string InteractionId { get; }

    public IReadOnlyList<NarrativeChoiceOption> Options { get; }

    public DateTimeOffset? ExpiresAt { get; }

    public bool AllowsMultiple { get; }

    public override GameCapability RequiredCapability => NarrativeCapabilities.Choices;
}

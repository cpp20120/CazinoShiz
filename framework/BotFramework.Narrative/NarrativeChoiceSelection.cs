namespace BotFramework.Narrative;

/// <summary>
/// Transport-neutral input emitted by a frontend when a player selects a
/// choice. A game maps it to its own command; Narrative never dispatches a
/// game command itself.
/// </summary>
public sealed record NarrativeChoiceSelection
{
    public NarrativeChoiceSelection(string interactionId, string optionId, string? value = null)
    {
        InteractionId = NarrativeValue.Required(interactionId, nameof(interactionId));
        OptionId = NarrativeValue.Required(optionId, nameof(optionId));
        Value = value;
    }

    public string InteractionId { get; }

    public string OptionId { get; }

    public string? Value { get; }
}

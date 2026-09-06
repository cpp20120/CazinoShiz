namespace BotFramework.Narrative;

/// <summary>
/// Transport-neutral destination for a narrative effect. A frontend maps these
/// stable application ids to its own conversation and recipient model.
/// </summary>
public sealed record NarrativeAddress
{
    public NarrativeAddress(string conversationId, string? recipientId = null)
    {
        ConversationId = NarrativeValue.Required(conversationId, nameof(conversationId));
        RecipientId = recipientId is null
            ? null
            : NarrativeValue.Required(recipientId, nameof(recipientId));
    }

    public string ConversationId { get; }

    public string? RecipientId { get; }
}

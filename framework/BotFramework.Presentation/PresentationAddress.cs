using BotFramework.Sdk.Execution;

namespace BotFramework.Presentation;

/// <summary>
/// Transport-neutral destination for a UI effect. A frontend maps these stable
/// application ids to its own conversation and recipient model.
/// </summary>
public sealed record PresentationAddress
{
    public PresentationAddress(
        string conversationId,
        string? recipientId = null,
        GameAudience? audience = null)
    {
        if (recipientId is not null && audience is not null)
            throw new ArgumentException("A presentation target cannot specify both a recipient and an audience.", nameof(audience));

        ConversationId = PresentationValue.Required(conversationId, nameof(conversationId));
        RecipientId = recipientId is null
            ? null
            : PresentationValue.Required(recipientId, nameof(recipientId));
        Audience = audience;
    }

    public string ConversationId { get; }

    public string? RecipientId { get; }

    /// <summary>
    /// Optional semantic audience for public, team, role or multi-player
    /// delivery. A frontend can resolve it from its game projection; null with
    /// no recipient retains the existing whole-conversation broadcast behavior.
    /// </summary>
    public GameAudience? Audience { get; }
}

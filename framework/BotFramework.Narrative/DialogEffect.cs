using BotFramework.Sdk.Execution;

namespace BotFramework.Narrative;

/// <summary>Appends one actor's line to the recipient's dialog stream.</summary>
public sealed record DialogEffect : NarrativeEffect
{
    public DialogEffect(
        NarrativeAddress target,
        NarrativeText line,
        string? speakerId = null,
        string? dialogId = null)
        : base(target)
    {
        Line = line ?? throw new ArgumentNullException(nameof(line));
        SpeakerId = speakerId is null ? null : NarrativeValue.Required(speakerId, nameof(speakerId));
        DialogId = dialogId is null ? null : NarrativeValue.Required(dialogId, nameof(dialogId));
    }

    public NarrativeText Line { get; }

    public string? SpeakerId { get; }

    public string? DialogId { get; }

    public override GameCapability RequiredCapability => NarrativeCapabilities.Dialog;
}

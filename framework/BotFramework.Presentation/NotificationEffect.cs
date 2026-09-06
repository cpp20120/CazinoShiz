using BotFramework.Sdk.Execution;

namespace BotFramework.Presentation;

/// <summary>Delivers one ordinary, localizable notification to a recipient.</summary>
public sealed record NotificationEffect : PresentationEffect
{
    public NotificationEffect(
        PresentationAddress target,
        PresentationText message,
        PresentationTone tone = PresentationTone.Info,
        string? notificationId = null)
        : base(target)
    {
        Message = message ?? throw new ArgumentNullException(nameof(message));
        Tone = tone;
        NotificationId = notificationId is null
            ? null
            : PresentationValue.Required(notificationId, nameof(notificationId));
    }

    public PresentationText Message { get; }

    public PresentationTone Tone { get; }

    /// <summary>Optional game-owned id used by a frontend for replacement or deduplication.</summary>
    public string? NotificationId { get; }

    public override GameCapability RequiredCapability => MessagingCapabilities.Messages;
}

using BotFramework.Sdk.Execution;

namespace BotFramework.Presentation;

/// <summary>Delivers media referenced by an application-owned opaque resource id.</summary>
public sealed record MediaEffect(
    PresentationAddress Target,
    PresentationMedia Media,
    PresentationText? Caption = null)
    : PresentationEffect(Target)
{
    public PresentationMedia Media { get; } = Media ?? throw new ArgumentNullException(nameof(Media));

    public override GameCapability RequiredCapability => MessagingCapabilities.Media;
}

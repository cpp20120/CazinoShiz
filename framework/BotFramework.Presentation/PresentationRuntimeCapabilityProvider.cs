using BotFramework.Sdk.Execution;

namespace BotFramework.Presentation;

/// <summary>Capabilities supplied by a complete ordinary UI presentation adapter.</summary>
public sealed class PresentationRuntimeCapabilityProvider : IGameRuntimeCapabilityProvider
{
    public GameCapabilitySet Capabilities { get; } = new(
    [
        MessagingCapabilities.Messages,
        MessagingCapabilities.RichResults,
        MessagingCapabilities.Input,
        MessagingCapabilities.Media,
    ]);
}

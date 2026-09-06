using BotFramework.Sdk.Execution;

namespace BotFramework.Narrative;

/// <summary>Capabilities supplied by a frontend presentation adapter.</summary>
public sealed class NarrativeRuntimeCapabilityProvider : IGameRuntimeCapabilityProvider
{
    public GameCapabilitySet Capabilities { get; } = new(
    [
        MessagingCapabilities.Messages,
        MessagingCapabilities.Input,
        NarrativeCapabilities.Scenes,
        NarrativeCapabilities.Dialog,
    ]);
}

using BotFramework.Sdk.Execution;

namespace BotFramework.Narrative.Host;

/// <summary>
/// Capabilities implemented by the persistent narrative read model without a
/// particular presentation frontend.
/// </summary>
public sealed class NarrativePersistenceRuntimeCapabilityProvider : IGameRuntimeCapabilityProvider
{
    public GameCapabilitySet Capabilities { get; } = new(
    [
        NarrativeCapabilities.Choices,
        NarrativeCapabilities.Flags,
        NarrativeCapabilities.Checkpoints,
    ]);
}

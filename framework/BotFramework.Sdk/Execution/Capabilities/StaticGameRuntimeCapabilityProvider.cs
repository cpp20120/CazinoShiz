namespace BotFramework.Sdk.Execution;

/// <summary>Simple provider for composition roots and test doubles.</summary>
public sealed class StaticGameRuntimeCapabilityProvider(IEnumerable<GameCapability> capabilities)
    : IGameRuntimeCapabilityProvider
{
    public GameCapabilitySet Capabilities { get; } = new(capabilities);
}

namespace BotFramework.Sdk.Execution;

/// <summary>Supplies the capabilities implemented by one Host, frontend or test adapter.</summary>
public interface IGameRuntimeCapabilityProvider
{
    GameCapabilitySet Capabilities { get; }
}

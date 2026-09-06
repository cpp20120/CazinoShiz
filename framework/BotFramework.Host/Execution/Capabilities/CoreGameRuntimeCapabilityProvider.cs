using BotFramework.Sdk.Execution;

namespace BotFramework.Host.Execution;

/// <summary>Capabilities implemented by the core transactional Host, without any frontend transport.</summary>
internal sealed class CoreGameRuntimeCapabilityProvider : IGameRuntimeCapabilityProvider
{
    public GameCapabilitySet Capabilities { get; } = new(
    [
        EconomyCapabilities.Wallet,
        EconomyCapabilities.Quota,
        PersistenceCapabilities.State,
        PersistenceCapabilities.Records,
        PersistenceCapabilities.Events,
        PersistenceCapabilities.Sessions,
        SchedulingCapabilities.Timers,
        TurnBasedCapabilities.Turns,
        TurnBasedCapabilities.Deadlines,
        CasinoCapabilities.Roll,
        CasinoCapabilities.Bet,
        CasinoCapabilities.Reward,
        CasinoCapabilities.Quota,
    ]);
}

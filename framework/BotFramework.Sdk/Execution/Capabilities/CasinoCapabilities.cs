namespace BotFramework.Sdk.Execution;

/// <summary>
/// Optional semantic capabilities for casino-style modules. They build on the
/// common economy, scheduling and persistence capabilities rather than adding
/// casino behavior to the Host core.
/// </summary>
public static class CasinoCapabilities
{
    public static GameCapability Roll { get; } = new("casino.roll");

    public static GameCapability Bet { get; } = new("casino.bet");

    public static GameCapability Reward { get; } = new("casino.reward");

    public static GameCapability Quota { get; } = new("casino.quota");
}

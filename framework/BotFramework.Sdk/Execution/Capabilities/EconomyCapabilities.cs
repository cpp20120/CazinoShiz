namespace BotFramework.Sdk.Execution;

/// <summary>Capabilities for wallet mutations and quota operations.</summary>
public static class EconomyCapabilities
{
    public static GameCapability Wallet { get; } = new("economy.wallet");

    public static GameCapability Quota { get; } = new("economy.quota");
}

namespace BotFramework.Sdk.Execution;

/// <summary>Capabilities for games driven by explicit turns and deadlines.</summary>
public static class TurnBasedCapabilities
{
    public static GameCapability Turns { get; } = new("turn-based.turns");

    public static GameCapability Deadlines { get; } = new("turn-based.deadlines");
}

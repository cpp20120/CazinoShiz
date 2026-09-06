namespace BotFramework.Sdk.Execution;

/// <summary>Capabilities for games driven by explicit phases and rounds.</summary>
public static class RoundBasedCapabilities
{
    public static GameCapability Rounds { get; } = new("round-based.rounds");

    public static GameCapability Phases { get; } = new("round-based.phases");

    public static GameCapability Deadlines { get; } = new("round-based.deadlines");
}

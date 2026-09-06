namespace BotFramework.Sdk.Execution;

/// <summary>Capabilities for auditable command execution history.</summary>
public static class ReplayCapabilities
{
    public static GameCapability ExecutionHistory { get; } = new("replay.execution-history");
}

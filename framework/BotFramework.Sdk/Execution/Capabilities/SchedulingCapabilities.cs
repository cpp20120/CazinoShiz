namespace BotFramework.Sdk.Execution;

/// <summary>Capabilities for durable timers and cancellation.</summary>
public static class SchedulingCapabilities
{
    public static GameCapability Timers { get; } = new("scheduling.timers");
}

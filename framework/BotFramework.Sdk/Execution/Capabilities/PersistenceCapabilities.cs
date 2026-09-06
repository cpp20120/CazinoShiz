namespace BotFramework.Sdk.Execution;

/// <summary>Capabilities for durable state, records, events and game sessions.</summary>
public static class PersistenceCapabilities
{
    public static GameCapability State { get; } = new("persistence.state");

    public static GameCapability Records { get; } = new("persistence.records");

    public static GameCapability Events { get; } = new("persistence.events");

    public static GameCapability Sessions { get; } = new("persistence.sessions");

    public static GameCapability Checkpoints { get; } = new("persistence.checkpoints");
}

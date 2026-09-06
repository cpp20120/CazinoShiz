namespace BotFramework.Sdk.Execution;

/// <summary>Capabilities reserved for a narrative extension package.</summary>
public static class NarrativeCapabilities
{
    public static GameCapability Scenes { get; } = new("narrative.scenes");

    public static GameCapability Choices { get; } = new("narrative.choices");

    public static GameCapability Dialog { get; } = new("narrative.dialog");

    public static GameCapability Flags { get; } = new("narrative.flags");

    public static GameCapability Checkpoints { get; } = new("narrative.checkpoints");
}

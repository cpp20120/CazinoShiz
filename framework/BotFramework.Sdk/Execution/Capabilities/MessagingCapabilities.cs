namespace BotFramework.Sdk.Execution;

/// <summary>Capabilities for semantic messages and collecting player input.</summary>
public static class MessagingCapabilities
{
    public static GameCapability Messages { get; } = new("messaging.messages");

    public static GameCapability Input { get; } = new("messaging.input");

    public static GameCapability RichResults { get; } = new("messaging.rich-results");

    public static GameCapability Media { get; } = new("messaging.media");
}

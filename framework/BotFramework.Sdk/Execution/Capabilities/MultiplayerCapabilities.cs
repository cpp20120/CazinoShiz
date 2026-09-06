namespace BotFramework.Sdk.Execution;

/// <summary>Capabilities for transport-neutral multiplayer game mechanics.</summary>
public static class MultiplayerCapabilities
{
    public static GameCapability Lobby { get; } = new("multiplayer.lobby");

    public static GameCapability Matchmaking { get; } = new("multiplayer.matchmaking");

    public static GameCapability Teams { get; } = new("multiplayer.teams");

    public static GameCapability Roles { get; } = new("multiplayer.roles");

    public static GameCapability Visibility { get; } = new("multiplayer.visibility");
}

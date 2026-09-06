namespace BotFramework.Sdk.Execution;

/// <summary>
/// Stable player and idempotency identity shared by a game command. A transport
/// maps its update/request to this value before invoking a game.
/// </summary>
public sealed record GameCommandContext
{
    public GameCommandContext(long userId, string displayName, long chatId, string commandId)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("Display name is required.", nameof(displayName));
        if (string.IsNullOrWhiteSpace(commandId))
            throw new ArgumentException("Command id is required.", nameof(commandId));

        UserId = userId;
        DisplayName = displayName;
        ChatId = chatId;
        CommandId = commandId;
    }

    public long UserId { get; }
    public string DisplayName { get; }
    public long ChatId { get; }
    public string CommandId { get; }
}

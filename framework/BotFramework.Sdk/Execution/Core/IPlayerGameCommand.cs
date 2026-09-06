namespace BotFramework.Sdk.Execution;

/// <summary>
/// Opt-in command shape understood by player-scoped execution descriptors.
/// Existing commands remain compatible and do not need to implement it.
/// </summary>
public interface IPlayerGameCommand
{
    long UserId { get; }
    string DisplayName { get; }
    long ChatId { get; }
    string CommandId { get; }
}

using BotFramework.Contracts.Messaging;

namespace BotFramework.Contracts.Wagering;

/// <summary>
/// Generic command emitted after a wager reservation. The payload is game
/// owned; Wagering only routes it and never interprets its rules.
/// </summary>
public sealed record WagerGameCommand(
    string OperationId,
    string BetId,
    string GameId,
    string Action,
    string PlayerId,
    long ChatId,
    string DisplayName,
    string GameInput,
    long ExpectedRevision,
    string CommandId,
    DateTimeOffset OccurredAt) : IIntegrationCommand, IIntegrationMessageRouted
{
    public string CommandType => "wager.game.command";
    public string? Topic => $"games.{GameId}.commands";
    public string? MessageKey => $"bet:{BetId}";
}

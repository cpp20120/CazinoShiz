using BotFramework.Contracts.Messaging;

namespace Games.Poker.Application.Wagering;

/// <summary>Integration command for the multi-player poker wager adapter.</summary>
public sealed record PokerWagerCommand(
    string OperationId,
    string BetId,
    string PlayerId,
    long ChatId,
    string DisplayName,
    string Action,
    string Payload,
    long Stake,
    string CommandId,
    DateTimeOffset OccurredAt) : IIntegrationCommand, IIntegrationMessageRouted
{
    public string CommandType => "poker.wager.command";
    public string? Topic => "games.poker.commands";
    public string? MessageKey => $"bet:{BetId}";
}

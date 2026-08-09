using BotFramework.Contracts.Messaging;

namespace Games.Blackjack.Contracts.Integration;

/// <summary>Closes an active wager when its turn deadline has elapsed.</summary>
public sealed record BlackjackWagerTimeout(
    string OperationId,
    string BetId,
    string PlayerId,
    long ChatId,
    string DisplayName,
    string CommandId,
    DateTimeOffset OccurredAt) : IIntegrationCommand, IIntegrationMessageRouted
{
    public string CommandType => "blackjack.wager.timeout";
    public string? Topic => "games.blackjack.commands";
    public string? MessageKey => $"bet:{BetId}";
}

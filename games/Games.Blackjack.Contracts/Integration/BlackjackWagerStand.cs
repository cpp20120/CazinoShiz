using BotFramework.Contracts.Messaging;

namespace Games.Blackjack.Contracts.Integration;

/// <summary>Ends a reserved blackjack wager by standing.</summary>
public sealed record BlackjackWagerStand(
    string OperationId,
    string BetId,
    string PlayerId,
    long ChatId,
    string DisplayName,
    long ExpectedRevision,
    string CommandId,
    DateTimeOffset OccurredAt) : IIntegrationCommand, IIntegrationMessageRouted
{
    public string CommandType => "blackjack.wager.stand";
    public string? Topic => "games.blackjack.commands";
    public string? MessageKey => $"bet:{BetId}";
}

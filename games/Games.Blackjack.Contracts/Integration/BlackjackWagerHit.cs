using BotFramework.Contracts.Messaging;

namespace Games.Blackjack.Contracts.Integration;

/// <summary>Draws one card from a reserved blackjack wager.</summary>
public sealed record BlackjackWagerHit(
    string OperationId,
    string BetId,
    string PlayerId,
    long ChatId,
    string DisplayName,
    long ExpectedRevision,
    string CommandId,
    DateTimeOffset OccurredAt) : IIntegrationCommand, IIntegrationMessageRouted
{
    public string CommandType => "blackjack.wager.hit";
    public string? Topic => "games.blackjack.commands";
    public string? MessageKey => $"bet:{BetId}";
}

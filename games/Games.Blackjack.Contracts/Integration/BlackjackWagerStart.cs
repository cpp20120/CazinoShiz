using BotFramework.Contracts.Messaging;

namespace Games.Blackjack.Contracts.Integration;

/// <summary>Starts blackjack after Wagering has reserved the stake.</summary>
public sealed record BlackjackWagerStart(
    string OperationId,
    string BetId,
    string PlayerId,
    long ChatId,
    string DisplayName,
    int HandTimeoutMs,
    string CommandId,
    DateTimeOffset OccurredAt) : IIntegrationCommand, IIntegrationMessageRouted
{
    public string CommandType => "blackjack.wager.start";
    public string? Topic => "games.blackjack.commands";
    public string? MessageKey => $"bet:{BetId}";
}

using BotFramework.Contracts.Messaging;

namespace Games.Horse.Application.Wagering;

public sealed record HorseWagerCommand(
    string OperationId,
    string BetId,
    string PlayerId,
    long ChatId,
    string DisplayName,
    string Payload,
    long Stake,
    string CommandId,
    DateTimeOffset OccurredAt) : IIntegrationCommand, IIntegrationMessageRouted
{
    public string CommandType => "horse.wager.place";
    public string? Topic => "games.horse.commands";
    public string? MessageKey => $"bet:{BetId}";
}

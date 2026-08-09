using BotFramework.Contracts.Messaging;

namespace Games.SecretHitler.Application.Wagering;

public sealed record SecretHitlerWagerCommand(
    string OperationId, string BetId, string PlayerId, long ChatId, string DisplayName,
    string Action, string Payload, long Stake, string CommandId, DateTimeOffset OccurredAt)
    : IIntegrationCommand, IIntegrationMessageRouted
{
    public string CommandType => "secret-hitler.wager.command";
    public string? Topic => "games.secret-hitler.commands";
    public string? MessageKey => $"bet:{BetId}";
}

using System.Text.Json;
using BotFramework.Contracts.Messaging;
using BotFramework.Contracts.Wagering;

namespace Games.Poker.Application.Wagering;

public sealed class PokerWagerGameCommandFactory : IWagerGameCommandFactory
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string GameId => "poker";

    public IIntegrationCommand Create(WagerRequested wager)
    {
        var input = JsonSerializer.Deserialize<WagerGameInput>(wager.GameInput, JsonOptions)
            ?? throw new InvalidOperationException($"Wager '{wager.OperationId}' has invalid poker input.");
        if (input.ChatId <= 0 || string.IsNullOrWhiteSpace(input.DisplayName))
            throw new InvalidOperationException($"Wager '{wager.OperationId}' has incomplete poker player context.");

        var action = input.Action.Trim().ToLowerInvariant();
        if (action is not ("create" or "join"))
            throw new InvalidOperationException($"Poker wager action '{input.Action}' is unsupported.");

        return new PokerWagerCommand(
            wager.OperationId,
            wager.BetId,
            wager.PlayerId,
            input.ChatId,
            input.DisplayName,
            action,
            input.Payload,
            wager.Terms.Stake,
            $"poker-wager:{wager.OperationId}:{action}",
            wager.OccurredAt);
    }
}

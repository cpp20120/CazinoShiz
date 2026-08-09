using System.Text.Json;
using BotFramework.Contracts.Messaging;
using BotFramework.Contracts.Wagering;

namespace Games.Horse.Application.Wagering;

public sealed class HorseWagerGameCommandFactory : IWagerGameCommandFactory
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public string GameId => "horse";

    public IIntegrationCommand Create(WagerRequested wager)
    {
        var input = JsonSerializer.Deserialize<WagerGameInput>(wager.GameInput, JsonOptions)
            ?? throw new InvalidOperationException($"Wager '{wager.OperationId}' has invalid horse input.");
        if (input.ChatId <= 0 || string.IsNullOrWhiteSpace(input.DisplayName))
            throw new InvalidOperationException($"Wager '{wager.OperationId}' has incomplete horse context.");
        if (!string.Equals(input.Action, "bet", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Horse wager action '{input.Action}' is unsupported.");
        return new HorseWagerCommand(wager.OperationId, wager.BetId, wager.PlayerId,
            input.ChatId, input.DisplayName, input.Payload, wager.Terms.Stake,
            $"horse-wager:{wager.OperationId}", wager.OccurredAt);
    }
}

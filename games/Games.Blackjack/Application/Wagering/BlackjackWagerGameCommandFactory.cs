using System.Text.Json;
using BotFramework.Contracts.Messaging;
using BotFramework.Contracts.Wagering;
using Games.Blackjack.Contracts.Integration;

namespace Games.Blackjack.Application.Wagering;

/// <summary>Converts an accepted Wagering operation into a game-only start command.</summary>
public sealed class BlackjackWagerGameCommandFactory : IWagerGameCommandFactory
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string GameId => "blackjack";

    public IIntegrationCommand Create(WagerRequested wager)
    {
        var input = JsonSerializer.Deserialize<BlackjackWagerStartInput>(wager.GameInput, JsonOptions)
            ?? throw new InvalidOperationException(
                $"Wager '{wager.OperationId}' has invalid blackjack start input.");
        if (input.ChatId == 0 || string.IsNullOrWhiteSpace(input.DisplayName) || input.HandTimeoutMs <= 0)
            throw new InvalidOperationException(
                $"Wager '{wager.OperationId}' has incomplete blackjack start input.");

        return new BlackjackWagerStart(
            wager.OperationId,
            wager.BetId,
            wager.PlayerId,
            input.ChatId,
            input.DisplayName,
            input.HandTimeoutMs,
            $"blackjack-wager:start:{wager.OperationId}",
            wager.OccurredAt);
    }
}

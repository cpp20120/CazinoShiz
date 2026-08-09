using System.Text.Json;
using BotFramework.Contracts.Messaging;
using BotFramework.Contracts.Wagering;

namespace BotFramework.Host.Wagering;

/// <summary>Routes all one-shot game wagers through the common game executor.</summary>
public sealed class WagerGameCommandFactory : IWagerGameCommandFactory
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public string GameId => "*";

    public IIntegrationCommand Create(WagerRequested wager)
    {
        var input = JsonSerializer.Deserialize<WagerGameInput>(wager.GameInput, JsonOptions)
            ?? throw new InvalidOperationException($"Wager '{wager.OperationId}' has invalid game input.");
        if (input.ChatId <= 0 || string.IsNullOrWhiteSpace(input.DisplayName))
            throw new InvalidOperationException($"Wager '{wager.OperationId}' has incomplete player context.");

        var action = string.IsNullOrWhiteSpace(input.Action) ? "play" : input.Action;
        return new WagerGameCommand(
            wager.OperationId,
            wager.BetId,
            wager.GameId,
            action,
            wager.PlayerId,
            input.ChatId,
            input.DisplayName,
            input.Payload,
            0,
            $"wager-game:{wager.OperationId}:{action}",
            wager.OccurredAt);
    }
}

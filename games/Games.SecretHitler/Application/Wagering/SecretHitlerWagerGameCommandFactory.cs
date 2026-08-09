using System.Text.Json;
using BotFramework.Contracts.Messaging;
using BotFramework.Contracts.Wagering;

namespace Games.SecretHitler.Application.Wagering;

public sealed class SecretHitlerWagerGameCommandFactory : IWagerGameCommandFactory
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public string GameId => "sh";

    public IIntegrationCommand Create(WagerRequested wager)
    {
        var input = JsonSerializer.Deserialize<WagerGameInput>(wager.GameInput, JsonOptions)
            ?? throw new InvalidOperationException($"Wager '{wager.OperationId}' has invalid Secret Hitler input.");
        if (input.ChatId <= 0 || string.IsNullOrWhiteSpace(input.DisplayName))
            throw new InvalidOperationException($"Wager '{wager.OperationId}' has incomplete Secret Hitler context.");
        var action = input.Action.Trim().ToLowerInvariant();
        if (action is not ("create" or "join"))
            throw new InvalidOperationException($"Secret Hitler wager action '{input.Action}' is unsupported.");
        return new SecretHitlerWagerCommand(wager.OperationId, wager.BetId, wager.PlayerId,
            input.ChatId, input.DisplayName, action, input.Payload, wager.Terms.Stake,
            $"sh-wager:{wager.OperationId}:{action}", wager.OccurredAt);
    }
}

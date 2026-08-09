using System.Text.Json;
using BotFramework.Contracts.Messaging;
using BotFramework.Contracts.Wagering;
using BotFramework.Host.Execution;
using Games.SecretHitler.Application.Execution;

namespace Games.SecretHitler.Application.Wagering;

public sealed class SecretHitlerWagerCommandHandler(
    IOutcomeOnlyGameExecutor<ShCreateCommand, SecretHitlerWagerState, ShCreateResult> create,
    IOutcomeOnlyGameExecutor<ShJoinCommand, SecretHitlerWagerState, ShJoinResult> joinExecutor,
    IIntegrationEventPublisher outcomes)
    : IIntegrationCommandHandler<SecretHitlerWagerCommand>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task HandleAsync(SecretHitlerWagerCommand command, CancellationToken ct)
    {
        try
        {
            var player = ParsePlayer(command.PlayerId);
            if (command.Action == "create")
            {
                var input = JsonSerializer.Deserialize<CreateInput>(command.Payload, JsonOptions)
                    ?? throw new InvalidOperationException("Secret Hitler create input is required.");
                if (input.BuyIn is > 0 && input.BuyIn != command.Stake)
                {
                    await RejectAsync(command, "stake_mismatch", ct);
                    return;
                }
                var result = await create.ExecuteAsync(new(new ShCreateCommand(
                    player, command.DisplayName, command.ChatId, command.ChatId,
                    command.CommandId, checked((int)command.Stake), [], command.BetId)), ct);
                if (result.Error != ShError.None) await RejectAsync(command, result.Error.ToString(), ct);
                return;
            }

            var joinInput = JsonSerializer.Deserialize<JoinInput>(command.Payload, JsonOptions)
                ?? throw new InvalidOperationException("Secret Hitler join input is required.");
            var joined = await joinExecutor.ExecuteAsync(new(new ShJoinCommand(
                joinInput.InviteCode.ToUpperInvariant(), player, command.DisplayName, command.ChatId,
                command.ChatId, command.CommandId, checked((int)command.Stake), [], command.BetId)), ct);
            if (joined.Error != ShError.None) await RejectAsync(command, joined.Error.ToString(), ct);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or OverflowException or InvalidOperationException)
        {
            await RejectAsync(command, "invalid_input", ct);
        }
    }

    private Task RejectAsync(SecretHitlerWagerCommand command, string error, CancellationToken ct) =>
        outcomes.PublishAsync(new GameOutcomeDeclared(command.BetId, "sh", command.PlayerId,
            "rejected", JsonSerializer.Serialize(new { payout = 0, error }), command.OccurredAt), ct);

    private static long ParsePlayer(string id) => long.TryParse(id, out var value) && value > 0
        ? value : throw new ArgumentException("Secret Hitler wager playerId must be numeric.");

    private sealed record CreateInput(int? BuyIn);
    private sealed record JoinInput(string InviteCode);
}

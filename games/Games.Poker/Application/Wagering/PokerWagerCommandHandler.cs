using System.Text.Json;
using BotFramework.Contracts.Messaging;
using BotFramework.Contracts.Wagering;
using BotFramework.Host.Execution;
using Games.Poker.Application.Execution;

namespace Games.Poker.Application.Wagering;

/// <summary>
/// Runs create/join against the existing table aggregate without loading a
/// primary wallet. A rejected game command becomes a refund outcome after its
/// game transaction has committed.
/// </summary>
public sealed class PokerWagerCommandHandler(
    IOutcomeOnlyGameExecutor<PokerCreateCommand, PokerWagerState, CreateResult> create,
    IOutcomeOnlyGameExecutor<PokerJoinCommand, PokerWagerState, JoinResult> join,
    IIntegrationEventPublisher outcomes)
    : IIntegrationCommandHandler<PokerWagerCommand>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task HandleAsync(PokerWagerCommand command, CancellationToken ct)
    {
        if (command.Action == "create")
        {
            var createInput = JsonSerializer.Deserialize<CreateInput>(command.Payload, JsonOptions)
                ?? throw new InvalidOperationException("Poker create input is required.");
            if (createInput.BuyIn <= 0 || createInput.BuyIn != command.Stake)
            {
                await PublishRejectedAsync(command, "stake_mismatch", ct);
                return;
            }
            var createResult = await create.ExecuteAsync(new GameExecutionEnvelope<PokerCreateCommand>(
                new PokerCreateCommand(
                    ParsePlayer(command.PlayerId), command.DisplayName, command.ChatId,
                    command.CommandId, createInput.BuyIn, createInput.SmallBlind, createInput.BigBlind, [], command.BetId)), ct);
            if (createResult.Error != PokerError.None)
                await PublishRejectedAsync(command, createResult.Error.ToString(), ct);
            return;
        }

        var joinInput = JsonSerializer.Deserialize<JoinInput>(command.Payload, JsonOptions)
            ?? throw new InvalidOperationException("Poker join input is required.");
        if (joinInput.BuyIn <= 0 || joinInput.BuyIn != command.Stake)
        {
            await PublishRejectedAsync(command, "stake_mismatch", ct);
            return;
        }
        var joinResult = await join.ExecuteAsync(new GameExecutionEnvelope<PokerJoinCommand>(
            new PokerJoinCommand(
                joinInput.InviteCode.ToUpperInvariant(), ParsePlayer(command.PlayerId), command.DisplayName,
                command.ChatId, command.CommandId, joinInput.BuyIn, joinInput.MaxPlayers, [], command.BetId)), ct);
        if (joinResult.Error != PokerError.None)
            await PublishRejectedAsync(command, joinResult.Error.ToString(), ct);
    }

    private Task PublishRejectedAsync(PokerWagerCommand command, string error, CancellationToken ct) =>
        outcomes.PublishAsync(
            new GameOutcomeDeclared(
                command.BetId,
                "poker",
                command.PlayerId,
                "rejected",
                JsonSerializer.Serialize(new { payout = 0, error }),
                command.OccurredAt),
            ct);

    private static long ParsePlayer(string playerId) =>
        long.TryParse(playerId, out var value) && value > 0
            ? value
            : throw new InvalidOperationException("Poker wager playerId must be a numeric user id.");

    private sealed record CreateInput(int BuyIn, int SmallBlind, int BigBlind);
    private sealed record JoinInput(string InviteCode, int BuyIn, int MaxPlayers);
}

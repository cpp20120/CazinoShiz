using BotFramework.Sdk.Execution;

namespace Games.Poker.Application.Execution;

public sealed class PokerJoinAction
    : IGameAction<PokerJoinCommand, PokerExecutionState, JoinResult>
{
    public GameDecision<PokerExecutionState, JoinResult> Decide(
        GameActionInput<PokerExecutionState, PokerJoinCommand> input)
    {
        var command = input.Command;
        if (input.State.Table is not { } source || source.Status == PokerTableStatus.Closed
            || source.ChatId != command.ChatId)
            return Reject(input.State, PokerError.TableNotFound, command.MaxPlayers);
        if (source.Status is not (PokerTableStatus.Seating or PokerTableStatus.HandComplete))
            return Reject(input.State, PokerError.HandInProgress, command.MaxPlayers);
        if (input.State.Seats.Any(seat => seat.UserId == command.ActorUserId))
            return Reject(input.State, PokerError.AlreadySeated, command.MaxPlayers);
        if (command.WagerBetId is null && input.State.ActorBalance < command.BuyIn)
            return Reject(input.State, PokerError.NotEnoughCoins, command.MaxPlayers);
        if (input.State.Seats.Count >= command.MaxPlayers)
            return Reject(input.State, PokerError.TableFull, command.MaxPlayers);

        var state = PokerExecutionRules.Clone(input.State);
        var used = state.Seats.Select(seat => seat.Position).ToHashSet();
        var position = 0;
        while (used.Contains(position)) position++;
        state.Seats.Add(new PokerSeat
        {
            InviteCode = source.InviteCode, Position = position, UserId = command.ActorUserId,
            DisplayName = command.DisplayName, Stack = command.BuyIn, ChatId = command.ChatId,
            JoinedAt = input.UtcNow.ToUnixTimeMilliseconds(), WagerBetId = command.WagerBetId,
        });
        return new(DecisionStatus.Accepted, state,
            new(PokerError.None, PokerExecutionRules.Snapshot(state), state.Seats.Count, command.MaxPlayers),
            [], [], [],
            [new PokerPlayerJoined(source.InviteCode, command.ActorUserId, position, command.BuyIn,
                input.UtcNow.ToUnixTimeMilliseconds())], [],
            CustomEffects: command.WagerBetId is null
                ? [WalletEconomyEffect.Debit(command.ActorUserId, command.ChatId, command.BuyIn, "poker.join")]
                : []);
    }

    private static GameDecision<PokerExecutionState, JoinResult> Reject(
        PokerExecutionState state, PokerError error, int maxPlayers) =>
        new(DecisionStatus.Rejected, state, new(error, null, 0, maxPlayers), [], [], [], [], [], error.ToString());
}

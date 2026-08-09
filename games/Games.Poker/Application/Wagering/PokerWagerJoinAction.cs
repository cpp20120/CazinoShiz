using BotFramework.Sdk.Execution;
using Games.Poker.Application.Execution;

namespace Games.Poker.Application.Wagering;

public sealed class PokerWagerJoinAction
    : IGameAction<PokerJoinCommand, PokerWagerState, JoinResult>
{
    public GameDecision<PokerWagerState, JoinResult> Decide(
        GameActionInput<PokerWagerState, PokerJoinCommand> input)
    {
        var command = input.Command;
        if (input.State.Table is not { } source || source.Status == PokerTableStatus.Closed
            || source.ChatId != command.ChatId)
            return Reject(input.State, PokerError.TableNotFound, command.MaxPlayers);
        if (source.Status is not (PokerTableStatus.Seating or PokerTableStatus.HandComplete))
            return Reject(input.State, PokerError.HandInProgress, command.MaxPlayers);
        if (input.State.Seats.Any(seat => seat.UserId == command.ActorUserId))
            return Reject(input.State, PokerError.AlreadySeated, command.MaxPlayers);
        if (input.State.Seats.Count >= command.MaxPlayers)
            return Reject(input.State, PokerError.TableFull, command.MaxPlayers);
        if (command.WagerBetId is null)
            return Reject(input.State, PokerError.InvalidAction, command.MaxPlayers);

        var state = new PokerWagerState(
            PokerExecutionRules.Clone(source),
            input.State.Seats.Select(PokerExecutionRules.Clone).ToList());
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
            new(PokerError.None, PokerExecutionRules.Snapshot(new PokerExecutionState(
                state.Table, state.Seats, null)), state.Seats.Count, command.MaxPlayers),
            [], [], [],
            [new PokerPlayerJoined(source.InviteCode, command.ActorUserId, position, command.BuyIn,
                input.UtcNow.ToUnixTimeMilliseconds())], []);
    }

    private static GameDecision<PokerWagerState, JoinResult> Reject(
        PokerWagerState state, PokerError error, int maxPlayers) =>
        new(DecisionStatus.Rejected, state, new(error, null, 0, maxPlayers), [], [], [], [], [], error.ToString());
}

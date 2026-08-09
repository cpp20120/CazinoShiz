using BotFramework.Sdk.Execution;
using Games.Poker.Application.Execution;

namespace Games.Poker.Application.Wagering;

public sealed class PokerWagerCreateAction
    : IGameAction<PokerCreateCommand, PokerWagerState, CreateResult>
{
    public GameDecision<PokerWagerState, CreateResult> Decide(
        GameActionInput<PokerWagerState, PokerCreateCommand> input)
    {
        if (input.State.Table is not null)
            return Reject(input.State, PokerError.TableAlreadyExists, input.Command.BuyIn);
        if (input.Command.WagerBetId is null)
            return Reject(input.State, PokerError.InvalidAction, input.Command.BuyIn);

        var command = input.Command;
        var now = input.UtcNow.ToUnixTimeMilliseconds();
        var code = PokerExecutionRules.InviteCode(input.Entropy.GetDouble(PokerExecutionRules.InviteEntropy));
        var table = new PokerTable
        {
            InviteCode = code, ChatId = command.ChatId, HostUserId = command.ActorUserId,
            Status = PokerTableStatus.Seating, Phase = PokerPhase.None,
            SmallBlind = command.SmallBlind, BigBlind = command.BigBlind,
            CreatedAt = now, LastActionAt = now,
        };
        var seat = new PokerSeat
        {
            InviteCode = code, Position = 0, UserId = command.ActorUserId,
            DisplayName = command.DisplayName, Stack = command.BuyIn,
            ChatId = command.ChatId, JoinedAt = now, WagerBetId = command.WagerBetId,
        };
        return new(DecisionStatus.Accepted, new(table, [seat]),
            new(PokerError.None, code, command.BuyIn), [], [], [],
            [new PokerTableCreated(code, command.ActorUserId, command.BuyIn, now)], []);
    }

    private static GameDecision<PokerWagerState, CreateResult> Reject(
        PokerWagerState state, PokerError error, int buyIn) =>
        new(DecisionStatus.Rejected, state, new(error, "", buyIn), [], [], [], [], [], error.ToString());
}

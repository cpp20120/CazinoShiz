using BotFramework.Sdk.Execution;

namespace Games.Poker.Application.Execution;

public sealed class PokerCreateAction
    : IGameAction<PokerCreateCommand, PokerExecutionState, CreateResult>
{
    public GameDecision<PokerExecutionState, CreateResult> Decide(
        GameActionInput<PokerExecutionState, PokerCreateCommand> input)
    {
        if (input.State.Table is not null)
            return Reject(input.State, PokerError.TableAlreadyExists, input.Command.BuyIn);
        if (input.Command.WagerBetId is null && input.State.ActorBalance < input.Command.BuyIn)
            return Reject(input.State, PokerError.NotEnoughCoins, input.Command.BuyIn);

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
        return new(DecisionStatus.Accepted, new(table, [seat], input.State.ActorBalance),
            new(PokerError.None, code, command.BuyIn), [], [], [],
            [new PokerTableCreated(code, command.ActorUserId, command.BuyIn, now)], [],
            CustomEffects: command.WagerBetId is null
                ? [WalletEconomyEffect.Debit(command.ActorUserId, command.ChatId, command.BuyIn, "poker.create")]
                : []);
    }

    private static GameDecision<PokerExecutionState, CreateResult> Reject(
        PokerExecutionState state, PokerError error, int buyIn) =>
        new(DecisionStatus.Rejected, state, new(error, "", buyIn), [], [], [], [], [], error.ToString());
}

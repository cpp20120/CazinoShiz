using BotFramework.Sdk.Execution;
using static Games.Horse.Domain.Rules.HorseResultHelpers;

namespace Games.Horse.Application.Execution;

public sealed class HorsePlaceBetAction
    : IGameAction<HorsePlaceBetCommand, HorseBetState, BetResult>
{
    public GameDecision<HorseBetState, BetResult> Decide(
        GameActionInput<HorseBetState, HorsePlaceBetCommand> input)
    {
        var command = input.Command;
        var balance = checked((int)input.Wallet.Balance);
        if (command.HorseId < 1 || command.HorseId > command.HorseCount)
            return Reject(input.State, BetFail(HorseError.InvalidHorseId), "invalid_horse");
        if (input.State.Bet is not null)
        {
            return Reject(input.State,
                new BetResult(HorseError.None, command.HorseId, command.Amount, balance),
                "duplicate_bet");
        }
        if (command.Amount <= 0 || command.Amount > input.Wallet.Balance)
            return Reject(input.State, BetFail(HorseError.InvalidAmount, command.HorseId, balance), "invalid_amount");

        var bet = new HorseBetRow(command.BetId, command.RaceDate, command.UserId,
            command.BalanceScopeId, command.HorseId - 1, command.Amount, command.WagerBetId);
        return new(
            DecisionStatus.Accepted,
            new HorseBetState(bet),
            new BetResult(HorseError.None, command.HorseId, command.Amount, balance - command.Amount),
            [EconomyEffect.Debit(command.Amount, "horse.bet")], [], [],
            [new HorseBetPlaced(command.UserId, command.HorseId, command.Amount, command.RaceDate,
                input.UtcNow.ToUnixTimeMilliseconds())], []);
    }

    private static GameDecision<HorseBetState, BetResult> Reject(
        HorseBetState state, BetResult result, string reason) =>
        new(DecisionStatus.Rejected, state, result, [], [], [], [], [], reason);
}

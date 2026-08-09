using BotFramework.Sdk.Execution;
using Games.Horse.Application.Execution;
using Games.Horse.Infrastructure.Persistence;
using static Games.Horse.Domain.Rules.HorseResultHelpers;

namespace Games.Horse.Application.Wagering;

/// <summary>Places a previously reserved wager into the horse pool without touching a wallet.</summary>
public sealed class HorseWagerPlaceBetAction
    : IGameAction<HorsePlaceBetCommand, HorseWagerState, BetResult>
{
    public GameDecision<HorseWagerState, BetResult> Decide(
        GameActionInput<HorseWagerState, HorsePlaceBetCommand> input)
    {
        var command = input.Command;
        if (string.IsNullOrWhiteSpace(command.WagerBetId))
            return Reject(input.State, BetFail(HorseError.InvalidAmount), "missing_bet_id");
        if (command.HorseId < 1 || command.HorseId > command.HorseCount)
            return Reject(input.State, BetFail(HorseError.InvalidHorseId), "invalid_horse");
        if (command.Amount <= 0)
            return Reject(input.State, BetFail(HorseError.InvalidAmount, command.HorseId), "invalid_amount");
        if (input.State.BetId is not null)
            return Reject(input.State, new(HorseError.None, command.HorseId, command.Amount, 0), "duplicate_bet");

        var bet = new HorseBetRow(command.BetId, command.RaceDate, command.UserId,
            command.BalanceScopeId, command.HorseId - 1, command.Amount, command.WagerBetId);
        return new(
            DecisionStatus.Accepted,
            new(bet.Id, bet.RaceDate, bet.UserId, bet.BalanceScopeId, command.HorseId),
            new(HorseError.None, command.HorseId, command.Amount, 0),
            [], [], [],
            [new HorseBetPlaced(command.UserId, command.HorseId, command.Amount, command.RaceDate,
                input.UtcNow.ToUnixTimeMilliseconds())], []);
    }

    private static GameDecision<HorseWagerState, BetResult> Reject(
        HorseWagerState state, BetResult result, string reason) =>
        new(DecisionStatus.Rejected, state, result, [], [], [], [], [], reason);
}

using BotFramework.Sdk.Execution;
using System.Text.Json;
using Games.Horse.Domain.Events;
using static Games.Horse.Domain.Rules.HorseResultHelpers;

namespace Games.Horse.Application.Execution;

public sealed class HorseRunAction
    : IGameAction<HorseRunCommand, HorseRaceState, RaceOutcome>
{
    public const string WinnerEntropy = "winner";

    public GameDecision<HorseRaceState, RaceOutcome> Decide(
        GameActionInput<HorseRaceState, HorseRunCommand> input)
    {
        var command = input.Command;
        if (!command.IsAdmin)
            return Reject(input.State, RaceFail(HorseError.NotAdmin), "not_admin");
        if (input.State.Winner is not null)
            return Reject(input.State, RaceFail(HorseError.NotEnoughBets), "already_completed");
        if (input.State.Bets.Count < command.MinBetsToRun)
            return Reject(input.State, RaceFail(HorseError.NotEnoughBets), "not_enough_bets");

        var stakes = Enumerable.Range(0, command.HorseCount).ToDictionary(index => index, _ => 0);
        foreach (var bet in input.State.Bets) stakes[bet.HorseId] += bet.Amount;
        var coefficients = HorseRules.GetCoefficients(stakes);
        var winner = Math.Min(command.HorseCount - 1,
            (int)(input.Entropy.GetDouble(WinnerEntropy) * command.HorseCount));
        var transactions = input.State.Bets
            .Where(bet => bet.HorseId == winner)
            .Select(bet => new RaceTransaction(bet.UserId, bet.BalanceScopeId,
                (int)Math.Floor(bet.Amount * coefficients[bet.HorseId])))
            .ToList();
        var payoutByWallet = input.State.Bets
            .Where(bet => bet.WagerBetId is null && bet.HorseId == winner)
            .GroupBy(bet => (bet.UserId, bet.BalanceScopeId))
            .Select(group => new RaceTransaction(group.Key.UserId, group.Key.BalanceScopeId,
                group.Sum(bet => checked((int)Math.Floor(bet.Amount * coefficients[bet.HorseId])))))
            .ToList();
        var wonByUser = payoutByWallet.GroupBy(item => item.UserId)
            .ToDictionary(group => group.Key, group => group.Sum(item => item.Amount));
        var participants = input.State.Bets.GroupBy(bet => bet.UserId)
            .Select(group => new RacerSummary(group.Key, group.Sum(bet => bet.Amount),
                wonByUser.GetValueOrDefault(group.Key)))
            .ToList();
        var betScopeIds = input.State.Bets.Select(bet => bet.BalanceScopeId)
            .Where(scopeId => scopeId != 0).Distinct().ToList();
        var resultScopes = command.Kind == HorseRunKind.Global
            ? betScopeIds.Prepend(0L).Distinct().ToArray()
            : [command.ResultScopeId];
        var pot = input.State.Bets.Sum(bet => bet.Amount);
        var occurredAt = input.UtcNow.ToUnixTimeMilliseconds();
        var wagerOutcomes = input.State.Bets
            .Where(bet => !string.IsNullOrWhiteSpace(bet.WagerBetId))
            .Select(bet =>
            {
                var payout = bet.HorseId == winner
                    ? checked((int)Math.Floor(bet.Amount * coefficients[bet.HorseId]))
                    : 0;
                return (IDomainEvent)new HorseWagerOutcomeDeclared(
                    bet.WagerBetId!,
                    bet.UserId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    payout > 0 ? "win" : "loss",
                    JsonSerializer.Serialize(new { payout }),
                    occurredAt);
            })
            .ToArray();

        return new(
            DecisionStatus.Accepted,
            new HorseRaceState(input.State.Bets, resultScopes, winner),
            new RaceOutcome(HorseError.None, winner, [], transactions, participants, betScopeIds,
                command.RaceDate),
            [], [], [],
            [new HorseRaceFinished(command.RaceDate, winner + 1, input.State.Bets.Count,
                transactions.Count, pot, occurredAt), ..wagerOutcomes],
            [],
            CustomEffects: payoutByWallet
                .Where(item => item.Amount > 0)
                .Select(item => (IGameEffect)WalletEconomyEffect.Credit(
                    item.UserId, item.BalanceScopeId, item.Amount, "horse.payout"))
                .ToArray());
    }

    private static GameDecision<HorseRaceState, RaceOutcome> Reject(
        HorseRaceState state, RaceOutcome result, string reason) =>
        new(DecisionStatus.Rejected, state, result, [], [], [], [], [], reason);
}

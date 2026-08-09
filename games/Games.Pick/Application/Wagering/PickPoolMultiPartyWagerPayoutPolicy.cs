using BotFramework.Contracts.Wagering;
using BotFramework.Host.Wagering;

namespace Games.Pick.Application.Wagering;

/// <summary>Wagering policy that settles the Pick pool after the draw fact.</summary>
public sealed class PickPoolMultiPartyWagerPayoutPolicy : IMultiPartyWagerPayoutPolicy
{
    private const string RulesVersion = "pick-pool.v1";
    private const int HouseFeeBasisPoints = 200;

    public string GameId => "pick-pool";

    public IReadOnlyList<MultiPartyWagerOutcome> Calculate(
        MultiPartyWagerRequest request,
        IReadOnlyList<MultiPartyWagerGameOutcome> outcomes)
    {
        if (request.Participants.Count < 2)
            throw new InvalidOperationException("A pick pool requires at least two participants.");
        if (request.Participants.Any(x => !string.Equals(x.Terms.RulesVersion, RulesVersion, StringComparison.Ordinal)))
            throw new InvalidOperationException($"Unsupported pick pool payout rules version; expected '{RulesVersion}'.");

        var pot = checked(request.Participants.Sum(x => x.Terms.Stake));
        var fee = checked(pot * HouseFeeBasisPoints / 10_000);
        var winnerPayout = checked(pot - fee);
        return outcomes.Select(outcome => new MultiPartyWagerOutcome(
            outcome.BetId,
            outcome.PlayerId,
            outcome.OutcomeCode,
            string.Equals(outcome.OutcomeCode, "win", StringComparison.Ordinal) ? winnerPayout : 0,
            outcome.Evidence)).ToArray();
    }
}

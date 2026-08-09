using BotFramework.Contracts.Wagering;
using BotFramework.Host.Wagering;

namespace Games.Challenges.Application.Wagering;

/// <summary>
/// Wagering payout policy for the challenge pool. The game adapter only
/// decides win/loss/tie; this policy is the first place that reads stake.
/// </summary>
public sealed class ChallengeMultiPartyWagerPayoutPolicy : IMultiPartyWagerPayoutPolicy
{
    private const string RulesVersion = "challenge.v1";
    private const int HouseFeeBasisPoints = 200;

    public string GameId => "challenge";

    public IReadOnlyList<MultiPartyWagerOutcome> Calculate(
        MultiPartyWagerRequest request,
        IReadOnlyList<MultiPartyWagerGameOutcome> outcomes)
    {
        if (request.Participants.Count != 2)
            throw new InvalidOperationException("A challenge requires exactly two participants.");
        if (request.Participants.Any(x => !string.Equals(x.Terms.RulesVersion, RulesVersion, StringComparison.Ordinal)))
            throw new InvalidOperationException($"Unsupported challenge payout rules version; expected '{RulesVersion}'.");

        var pot = checked(request.Participants.Sum(x => x.Terms.Stake));
        var fee = checked(pot * HouseFeeBasisPoints / 10_000);
        var winnerPayout = checked(pot - fee);
        return outcomes.Select(outcome =>
        {
            var participant = request.Participants.Single(x => string.Equals(x.BetId, outcome.BetId, StringComparison.Ordinal));
            var payout = outcome.OutcomeCode switch
            {
                "tie" => participant.Terms.Stake,
                "win" => winnerPayout,
                _ => 0,
            };
            return new MultiPartyWagerOutcome(outcome.BetId, outcome.PlayerId, outcome.OutcomeCode, payout, outcome.Evidence);
        }).ToArray();
    }
}

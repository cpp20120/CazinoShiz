using BotFramework.Contracts.Wagering;
using Games.Challenges.Application.Wagering;
using Games.Pick.Application.Wagering;
using Xunit;

namespace CasinoShiz.Tests;

public sealed class MultiPartyWagerAdapterTests
{
    [Fact]
    public async Task Challenge_ProducesDeterministicGameFacts_AndPolicyConservesThePot()
    {
        var gameRequest = CreateGameRequest("challenge-e2e", "challenge", "{\"maxRoll\":6}");
        var wagerRequest = CreateWagerRequest(gameRequest);
        var adapter = new ChallengeMultiPartyWagerAdapter();
        var policy = new ChallengeMultiPartyWagerPayoutPolicy();

        var first = await adapter.ExecuteAsync(gameRequest, CancellationToken.None);
        var second = await adapter.ExecuteAsync(gameRequest, CancellationToken.None);
        var settlements = policy.Calculate(wagerRequest, first);

        Assert.Equal(first, second);
        Assert.Equal(2, first.Count);
        Assert.Equal(196, settlements.Sum(x => x.Payout));
        Assert.Single(first, x => x.OutcomeCode == "win");
        Assert.Single(first, x => x.OutcomeCode == "loss");
        Assert.All(first, x => Assert.DoesNotContain("payout", x.Evidence, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PickPool_ProducesOneWinner_AndPolicyConservesThePot()
    {
        var gameRequest = CreateGameRequest("pick-pool-e2e", "pick-pool", "{}");
        var wagerRequest = CreateWagerRequest(gameRequest, 3);
        var adapter = new PickPoolMultiPartyWagerAdapter();
        var policy = new PickPoolMultiPartyWagerPayoutPolicy();

        var gameOutcomes = await adapter.ExecuteAsync(gameRequest with
        {
            Participants = wagerRequest.Participants
                .Select(x => new MultiPartyWagerGameParticipant(x.BetId, x.PlayerId))
                .ToArray(),
        }, CancellationToken.None);
        var settlements = policy.Calculate(wagerRequest, gameOutcomes);

        Assert.Equal(3, gameOutcomes.Count);
        Assert.Equal(294, settlements.Sum(x => x.Payout));
        Assert.Single(gameOutcomes, x => x.OutcomeCode == "win");
        Assert.Equal(2, gameOutcomes.Count(x => x.OutcomeCode == "loss"));
        Assert.All(gameOutcomes, x => Assert.DoesNotContain("fee", x.Evidence, StringComparison.OrdinalIgnoreCase));
    }

    private static MultiPartyWagerGameRequest CreateGameRequest(
        string workflowId,
        string gameId,
        string gameInput,
        int participantCount = 2) =>
        new(
            workflowId,
            gameId,
            gameInput,
            Enumerable.Range(1, participantCount)
                .Select(index => new MultiPartyWagerGameParticipant($"bet-{index}", $"player-{index}"))
                .ToArray());

    private static MultiPartyWagerRequest CreateWagerRequest(
        MultiPartyWagerGameRequest gameRequest,
        int participantCount = 2) =>
        new(
            gameRequest.WorkflowId,
            gameRequest.GameId,
            gameRequest.GameInput,
            Enumerable.Range(1, participantCount)
                .Select(index => new MultiPartyWagerParticipant(
                    $"operation-{index}",
                    $"bet-{index}",
                    $"player-{index}",
                    new WagerTermsSnapshot($"{gameRequest.GameId}.v1", 100, "coins", "standard")))
                .ToArray(),
            "write-load",
            "42",
            new DateTimeOffset(2026, 8, 9, 10, 0, 0, TimeSpan.Zero));
}

using Games.Blackjack;
using Microsoft.Extensions.Options;
using Xunit;

namespace CasinoShiz.Tests;

public class BlackjackServiceTests
{
    private static BlackjackService MakeService(
        FakeEconomicsService? economics = null,
        InMemoryBlackjackHandStore? hands = null,
        int minBet = 1,
        int maxBet = 1_000) =>
        new(
            hands ?? new InMemoryBlackjackHandStore(),
            economics ?? new FakeEconomicsService(),
            new NullAnalyticsService(),
            new NullEventBus(),
            Options.Create(new BlackjackOptions { MinBet = minBet, MaxBet = maxBet }));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task StartAsync_BetBelowMin_ReturnsInvalidBet(int bet)
    {
        var svc = MakeService(minBet: 1);
        var result = await svc.StartAsync(1, "u", 100, bet, default);
        Assert.Equal(BlackjackError.InvalidBet, result.Error);
        Assert.Null(result.Snapshot);
    }

    [Fact]
    public async Task StartAsync_BetAboveMax_ReturnsInvalidBet()
    {
        var svc = MakeService(maxBet: 100);
        var result = await svc.StartAsync(1, "u", 100, bet: 200, default);
        Assert.Equal(BlackjackError.InvalidBet, result.Error);
    }

    [Fact]
    public async Task StartAsync_InsufficientBalance_ReturnsNotEnoughCoins()
    {
        var econ = new FakeEconomicsService { StartingBalance = 5 };
        var svc = MakeService(economics: econ);
        var result = await svc.StartAsync(1, "u", 100, bet: 100, default);
        Assert.Equal(BlackjackError.NotEnoughCoins, result.Error);
    }

    [Fact]
    public async Task StartAsync_HandAlreadyInProgress_ReturnsHandInProgress()
    {
        var hands = new InMemoryBlackjackHandStore();
        var svc = MakeService(hands: hands);
        await svc.StartAsync(1, "u", 100, bet: 10, default);

        // Check if the result was a blackjack (immediate settle), if so skip
        var snapshot = await svc.GetSnapshotAsync(1, default);
        if (snapshot.snapshot == null)
            return; // Natural blackjack settled immediately — no active hand to test

        var result = await svc.StartAsync(1, "u", 100, bet: 10, default);
        Assert.Equal(BlackjackError.HandInProgress, result.Error);
    }

    [Fact]
    public async Task StartAsync_ValidBet_DebitsFromBalance()
    {
        var econ = new FakeEconomicsService();
        var svc = MakeService(economics: econ);
        await svc.StartAsync(1, "u", 100, bet: 50, default);
        Assert.Single(econ.Debits);
        Assert.Equal(50, econ.Debits[0].Amount);
    }

    [Fact]
    public async Task StartAsync_ValidBet_ReturnsSnapshotOrBlackjack()
    {
        var svc = MakeService();
        var result = await svc.StartAsync(1, "u", 100, bet: 10, default);
        Assert.Equal(BlackjackError.None, result.Error);
        Assert.NotNull(result.Snapshot);
    }

    [Fact]
    public async Task StartAsync_PlayerBlackjack_ImmediatelySettles()
    {
        // Run enough hands to observe at least one natural blackjack payout
        var wins = 0;
        for (var i = 0; i < 200; i++)
        {
            var econ = new FakeEconomicsService();
            var svc = MakeService(economics: econ);
            var result = await svc.StartAsync(1, "u", 100, bet: 10, default);
            if (result.Snapshot?.Outcome == BlackjackOutcome.PlayerBlackjack)
            {
                wins++;
                // Natural blackjack pays 2.5x bet
                Assert.Equal(25, result.Snapshot.Payout);
                Assert.Single(econ.Credits);
            }
        }
        // With a single deck, ~4.8% natural blackjack rate. 200 trials should produce at least one.
        Assert.True(wins >= 1, "Expected at least one natural blackjack in 200 hands");
    }

    [Fact]
    public async Task HitAsync_NoActiveHand_ReturnsNoActiveHand()
    {
        var svc = MakeService();
        var result = await svc.HitAsync(42, default);
        Assert.Equal(BlackjackError.NoActiveHand, result.Error);
    }

    [Fact]
    public async Task StandAsync_NoActiveHand_ReturnsNoActiveHand()
    {
        var svc = MakeService();
        var result = await svc.StandAsync(42, default);
        Assert.Equal(BlackjackError.NoActiveHand, result.Error);
    }

    [Fact]
    public async Task DoubleAsync_NoActiveHand_ReturnsNoActiveHand()
    {
        var svc = MakeService();
        var result = await svc.DoubleAsync(42, default);
        Assert.Equal(BlackjackError.NoActiveHand, result.Error);
    }

    [Fact]
    public async Task StandAsync_ActiveHand_SettlesWithOutcome()
    {
        var hands = new InMemoryBlackjackHandStore();
        var svc = MakeService(hands: hands);

        BlackjackResult? standResult = null;
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var econ = new FakeEconomicsService();
            var freshSvc = MakeService(economics: econ, hands: new InMemoryBlackjackHandStore());
            var start = await freshSvc.StartAsync(1, "u", 100, bet: 10, default);
            if (start.Snapshot?.Outcome != null) continue; // natural blackjack, no active hand

            standResult = await freshSvc.StandAsync(1, default);
            break;
        }

        Assert.NotNull(standResult);
        Assert.Equal(BlackjackError.None, standResult!.Error);
        Assert.NotNull(standResult.Snapshot?.Outcome);
    }
}

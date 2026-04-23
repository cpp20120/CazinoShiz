using Games.Football;
using Microsoft.Extensions.Options;
using Xunit;

namespace CasinoShiz.Tests;

public class FootballServiceTests
{
    private static FootballService MakeService(
        FakeEconomicsService? economics = null,
        InMemoryFootballBetStore? bets = null) =>
        new(
            economics ?? new FakeEconomicsService(),
            new NullAnalyticsService(),
            bets ?? new InMemoryFootballBetStore(),
            new NullEventBus(),
            Options.Create(new FootballOptions()));

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 0)]
    [InlineData(3, 0)]
    [InlineData(4, 2)]
    [InlineData(5, 2)]
    public async Task ThrowAsync_ReturnsCorrectMultiplier(int face, int expectedMultiplier)
    {
        var bets = new InMemoryFootballBetStore();
        var svc = MakeService(bets: bets);
        await svc.PlaceBetAsync(1, "u", 100, 50, default);
        var result = await svc.ThrowAsync(1, "u", 100, face, default);
        Assert.Equal(expectedMultiplier, result.Multiplier);
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 0)]
    [InlineData(3, 0)]
    [InlineData(4, 100)]
    [InlineData(5, 100)]
    public async Task ThrowAsync_ReturnsCorrectPayout(int face, int expectedPayout)
    {
        var bets = new InMemoryFootballBetStore();
        var svc = MakeService(bets: bets);
        await svc.PlaceBetAsync(1, "u", 100, 50, default);
        var result = await svc.ThrowAsync(1, "u", 100, face, default);
        Assert.Equal(expectedPayout, result.Payout);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    public async Task ThrowAsync_WinningFace_CreditsPayout(int face)
    {
        var econ = new FakeEconomicsService();
        var bets = new InMemoryFootballBetStore();
        var svc = MakeService(economics: econ, bets: bets);
        await svc.PlaceBetAsync(1, "u", 100, 50, default);
        await svc.ThrowAsync(1, "u", 100, face, default);
        Assert.Single(econ.Credits);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task ThrowAsync_LosingFace_NoCredit(int face)
    {
        var econ = new FakeEconomicsService();
        var bets = new InMemoryFootballBetStore();
        var svc = MakeService(economics: econ, bets: bets);
        await svc.PlaceBetAsync(1, "u", 100, 50, default);
        await svc.ThrowAsync(1, "u", 100, face, default);
        Assert.Empty(econ.Credits);
    }

    [Fact]
    public async Task ThrowAsync_UnknownFace_NoPayout()
    {
        var bets = new InMemoryFootballBetStore();
        var svc = MakeService(bets: bets);
        await svc.PlaceBetAsync(1, "u", 100, 50, default);
        var result = await svc.ThrowAsync(1, "u", 100, 99, default);
        Assert.Equal(0, result.Payout);
        Assert.Equal(0, result.Multiplier);
    }

    [Fact]
    public async Task ThrowAsync_PublishesThrowCompletedEvent()
    {
        var bus = new NullEventBus();
        var bets = new InMemoryFootballBetStore();
        var svc = new FootballService(new FakeEconomicsService(), new NullAnalyticsService(), bets, bus, Options.Create(new FootballOptions()));
        await svc.PlaceBetAsync(1, "u", 100, 50, default);
        await svc.ThrowAsync(1, "u", 100, 4, default);
        Assert.Single(bus.Published);
        Assert.IsType<FootballThrowCompleted>(bus.Published[0]);
    }

    [Fact]
    public void Multipliers_ContainsAllFiveFaces()
    {
        for (var face = 1; face <= 5; face++)
            Assert.True(FootballService.Multipliers.ContainsKey(face));
    }
}

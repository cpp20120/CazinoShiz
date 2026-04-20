using Games.Basketball;
using Games.Bowling;
using Games.Darts;
using Games.DiceCube;
using Xunit;

namespace CasinoShiz.Tests;

public class DiceCubeMultiplierTests
{
    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 0)]
    [InlineData(3, 0)]
    [InlineData(4, 2)]
    [InlineData(5, 3)]
    [InlineData(6, 5)]
    public void Multipliers_CorrectByFace(int face, int expected)
    {
        Assert.Equal(expected, DiceCubeService.Multipliers[face]);
    }

    [Fact]
    public async Task PlaceBetAsync_InvalidAmount_ReturnsFail()
    {
        var svc = new DiceCubeService(new FakeEconomicsService(), new NullAnalyticsService(),
            new InMemoryDiceCubeBetStore(), new NullEventBus());
        var result = await svc.PlaceBetAsync(1, "u", 100, amount: 0, default);
        Assert.Equal(CubeBetError.InvalidAmount, result.Error);
    }

    [Fact]
    public async Task PlaceBetAsync_InsufficientBalance_ReturnsFail()
    {
        var econ = new FakeEconomicsService { StartingBalance = 10 };
        var svc = new DiceCubeService(econ, new NullAnalyticsService(),
            new InMemoryDiceCubeBetStore(), new NullEventBus());
        var result = await svc.PlaceBetAsync(1, "u", 100, amount: 100, default);
        Assert.Equal(CubeBetError.NotEnoughCoins, result.Error);
    }

    [Fact]
    public async Task PlaceBetAsync_DoubleBet_ReturnsAlreadyPending()
    {
        var store = new InMemoryDiceCubeBetStore();
        var svc = new DiceCubeService(new FakeEconomicsService(), new NullAnalyticsService(),
            store, new NullEventBus());
        await svc.PlaceBetAsync(1, "u", 100, amount: 50, default);
        var result = await svc.PlaceBetAsync(1, "u", 100, amount: 50, default);
        Assert.Equal(CubeBetError.AlreadyPending, result.Error);
    }

    [Fact]
    public async Task RollAsync_NoBet_ReturnsNoBet()
    {
        var svc = new DiceCubeService(new FakeEconomicsService(), new NullAnalyticsService(),
            new InMemoryDiceCubeBetStore(), new NullEventBus());
        var result = await svc.RollAsync(1, "u", 100, face: 6, default);
        Assert.Equal(CubeRollOutcome.NoBet, result.Outcome);
    }

    [Fact]
    public async Task RollAsync_WithBetFace6_CreditsX5()
    {
        var econ = new FakeEconomicsService();
        var store = new InMemoryDiceCubeBetStore();
        var svc = new DiceCubeService(econ, new NullAnalyticsService(), store, new NullEventBus());
        await svc.PlaceBetAsync(1, "u", 100, amount: 100, default);
        var result = await svc.RollAsync(1, "u", 100, face: 6, default);
        Assert.Equal(CubeRollOutcome.Rolled, result.Outcome);
        Assert.Equal(5, result.Multiplier);
        Assert.Equal(500, result.Payout);
    }

    [Fact]
    public async Task RollAsync_WithBetFace1_ZeroPayout()
    {
        var econ = new FakeEconomicsService();
        var store = new InMemoryDiceCubeBetStore();
        var svc = new DiceCubeService(econ, new NullAnalyticsService(), store, new NullEventBus());
        await svc.PlaceBetAsync(1, "u", 100, amount: 100, default);
        var result = await svc.RollAsync(1, "u", 100, face: 1, default);
        Assert.Equal(CubeRollOutcome.Rolled, result.Outcome);
        Assert.Equal(0, result.Payout);
        Assert.Empty(econ.Credits);
    }
}

public class BasketballMultiplierTests
{
    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 0)]
    [InlineData(3, 0)]
    [InlineData(4, 2)]
    [InlineData(5, 3)]
    public void Multipliers_CorrectByFace(int face, int expected)
    {
        Assert.Equal(expected, BasketballService.Multipliers[face]);
    }

    [Fact]
    public async Task PlaceBetAsync_NegativeAmount_ReturnsFail()
    {
        var svc = new BasketballService(new FakeEconomicsService(), new NullAnalyticsService(),
            new InMemoryBasketballBetStore(), new NullEventBus());
        var result = await svc.PlaceBetAsync(1, "u", 100, amount: -5, default);
        Assert.Equal(BasketballBetError.InvalidAmount, result.Error);
    }

    [Fact]
    public async Task ThrowAsync_WithBetFace5_CreditsX3()
    {
        var econ = new FakeEconomicsService();
        var store = new InMemoryBasketballBetStore();
        var svc = new BasketballService(econ, new NullAnalyticsService(), store, new NullEventBus());
        await svc.PlaceBetAsync(1, "u", 100, amount: 100, default);
        var result = await svc.ThrowAsync(1, "u", 100, face: 5, default);
        Assert.Equal(BasketballThrowOutcome.Thrown, result.Outcome);
        Assert.Equal(3, result.Multiplier);
        Assert.Equal(300, result.Payout);
    }

    [Fact]
    public async Task ThrowAsync_WithBetFace2_ZeroPayout()
    {
        var econ = new FakeEconomicsService();
        var store = new InMemoryBasketballBetStore();
        var svc = new BasketballService(econ, new NullAnalyticsService(), store, new NullEventBus());
        await svc.PlaceBetAsync(1, "u", 100, amount: 50, default);
        var result = await svc.ThrowAsync(1, "u", 100, face: 2, default);
        Assert.Equal(0, result.Payout);
        Assert.Empty(econ.Credits);
    }
}

public class BowlingMultiplierTests
{
    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 0)]
    [InlineData(3, 0)]
    [InlineData(4, 2)]
    [InlineData(5, 3)]
    [InlineData(6, 6)]
    public void Multipliers_CorrectByFace(int face, int expected)
    {
        Assert.Equal(expected, BowlingService.Multipliers[face]);
    }

    [Fact]
    public async Task RollAsync_Strike_CreditsX6()
    {
        var econ = new FakeEconomicsService();
        var store = new InMemoryBowlingBetStore();
        var svc = new BowlingService(econ, new NullAnalyticsService(), store, new NullEventBus());
        await svc.PlaceBetAsync(1, "u", 100, amount: 50, default);
        var result = await svc.RollAsync(1, "u", 100, face: 6, default);
        Assert.Equal(BowlingRollOutcome.Rolled, result.Outcome);
        Assert.Equal(300, result.Payout);
    }

    [Fact]
    public async Task PlaceBetAsync_DoubleBet_ReturnsAlreadyPending()
    {
        var store = new InMemoryBowlingBetStore();
        var svc = new BowlingService(new FakeEconomicsService(), new NullAnalyticsService(),
            store, new NullEventBus());
        await svc.PlaceBetAsync(1, "u", 100, amount: 50, default);
        var result = await svc.PlaceBetAsync(1, "u", 100, amount: 50, default);
        Assert.Equal(BowlingBetError.AlreadyPending, result.Error);
    }
}

public class DartsMultiplierTests
{
    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 0)]
    [InlineData(3, 0)]
    [InlineData(4, 2)]
    [InlineData(5, 3)]
    [InlineData(6, 6)]
    public void Multipliers_CorrectByFace(int face, int expected)
    {
        Assert.Equal(expected, DartsService.Multipliers[face]);
    }

    [Fact]
    public async Task ThrowAsync_Bullseye_CreditsX6()
    {
        var econ = new FakeEconomicsService();
        var store = new InMemoryDartsBetStore();
        var svc = new DartsService(econ, new NullAnalyticsService(), store, new NullEventBus());
        await svc.PlaceBetAsync(1, "u", 100, amount: 100, default);
        var result = await svc.ThrowAsync(1, "u", 100, face: 6, default);
        Assert.Equal(DartsThrowOutcome.Thrown, result.Outcome);
        Assert.Equal(600, result.Payout);
    }

    [Fact]
    public async Task ThrowAsync_NoBet_ReturnsNoBet()
    {
        var svc = new DartsService(new FakeEconomicsService(), new NullAnalyticsService(),
            new InMemoryDartsBetStore(), new NullEventBus());
        var result = await svc.ThrowAsync(1, "u", 100, face: 6, default);
        Assert.Equal(DartsThrowOutcome.NoBet, result.Outcome);
    }
}

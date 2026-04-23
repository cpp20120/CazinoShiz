using BotFramework.Sdk;
using Games.Basketball;
using Games.Bowling;
using Games.Darts;
using Games.Football;
using Games.DiceCube;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Xunit;

namespace CasinoShiz.Tests;

[Collection("MiniGameSession")]
public class DiceCubeMultiplierTests
{
    public DiceCubeMultiplierTests() => BotMiniGameSession.DangerousResetAllForTests();

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 0)]
    [InlineData(3, 0)]
    [InlineData(4, 1)]
    [InlineData(5, 2)]
    [InlineData(6, 2)]
    public void Multipliers_CorrectByFace(int face, int expected)
    {
        var m = DiceCubeService.BuildMultipliers(new DiceCubeOptions());
        Assert.Equal(expected, m[face]);
    }

    [Fact]
    public async Task PlaceBetAsync_InvalidAmount_ReturnsFail()
    {
        var svc = new DiceCubeService(new FakeEconomicsService(), new NullAnalyticsService(),
            new InMemoryDiceCubeBetStore(), new NullEventBus(), new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new DiceCubeOptions()), new NullMiniGameSessionGhostHeal());
        var result = await svc.PlaceBetAsync(1, "u", 100, amount: 0, default);
        Assert.Equal(CubeBetError.InvalidAmount, result.Error);
    }

    [Fact]
    public async Task PlaceBetAsync_InsufficientBalance_ReturnsFail()
    {
        var econ = new FakeEconomicsService { StartingBalance = 10 };
        var svc = new DiceCubeService(econ, new NullAnalyticsService(),
            new InMemoryDiceCubeBetStore(), new NullEventBus(), new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new DiceCubeOptions()), new NullMiniGameSessionGhostHeal());
        var result = await svc.PlaceBetAsync(1, "u", 100, amount: 100, default);
        Assert.Equal(CubeBetError.NotEnoughCoins, result.Error);
    }

    [Fact]
    public async Task PlaceBetAsync_DoubleBet_ReturnsAlreadyPending()
    {
        var store = new InMemoryDiceCubeBetStore();
        var svc = new DiceCubeService(new FakeEconomicsService(), new NullAnalyticsService(),
            store, new NullEventBus(), new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new DiceCubeOptions()), new NullMiniGameSessionGhostHeal());
        await svc.PlaceBetAsync(1, "u", 100, amount: 50, default);
        var result = await svc.PlaceBetAsync(1, "u", 100, amount: 50, default);
        Assert.Equal(CubeBetError.AlreadyPending, result.Error);
    }

    [Fact]
    public async Task RollAsync_NoBet_ReturnsNoBet()
    {
        var svc = new DiceCubeService(new FakeEconomicsService(), new NullAnalyticsService(),
            new InMemoryDiceCubeBetStore(), new NullEventBus(), new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new DiceCubeOptions()), new NullMiniGameSessionGhostHeal());
        var result = await svc.RollAsync(1, "u", 100, face: 6, default);
        Assert.Equal(CubeRollOutcome.NoBet, result.Outcome);
    }

    [Fact]
    public async Task RollAsync_WithBetFace6_CreditsX2()
    {
        var econ = new FakeEconomicsService();
        var store = new InMemoryDiceCubeBetStore();
        var svc = new DiceCubeService(econ, new NullAnalyticsService(), store, new NullEventBus(), new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new DiceCubeOptions()), new NullMiniGameSessionGhostHeal());
        await svc.PlaceBetAsync(1, "u", 100, amount: 100, default);
        var result = await svc.RollAsync(1, "u", 100, face: 6, default);
        Assert.Equal(CubeRollOutcome.Rolled, result.Outcome);
        Assert.Equal(2, result.Multiplier);
        Assert.Equal(200, result.Payout);
    }

    [Fact]
    public async Task RollAsync_WithBetFace1_ZeroPayout()
    {
        var econ = new FakeEconomicsService();
        var store = new InMemoryDiceCubeBetStore();
        var svc = new DiceCubeService(econ, new NullAnalyticsService(), store, new NullEventBus(), new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new DiceCubeOptions()), new NullMiniGameSessionGhostHeal());
        await svc.PlaceBetAsync(1, "u", 100, amount: 100, default);
        var result = await svc.RollAsync(1, "u", 100, face: 1, default);
        Assert.Equal(CubeRollOutcome.Rolled, result.Outcome);
        Assert.Equal(0, result.Payout);
        Assert.Empty(econ.Credits);
    }
}

public class BasketballMultiplierTests
{
    public BasketballMultiplierTests() => BotMiniGameSession.DangerousResetAllForTests();

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 0)]
    [InlineData(3, 0)]
    [InlineData(4, 2)]
    [InlineData(5, 2)]
    public void Multipliers_CorrectByFace(int face, int expected)
    {
        Assert.Equal(expected, BasketballService.Multipliers[face]);
    }

    [Fact]
    public async Task PlaceBetAsync_NegativeAmount_ReturnsFail()
    {
        var svc = new BasketballService(new FakeEconomicsService(), new NullAnalyticsService(),
            new InMemoryBasketballBetStore(), new NullEventBus(), Options.Create(new BasketballOptions()),
            new NullMiniGameSessionGhostHeal());
        var result = await svc.PlaceBetAsync(1, "u", 100, amount: -5, default);
        Assert.Equal(BasketballBetError.InvalidAmount, result.Error);
    }

    [Fact]
    public async Task ThrowAsync_WithBetFace5_CreditsX2()
    {
        var econ = new FakeEconomicsService();
        var store = new InMemoryBasketballBetStore();
        var svc = new BasketballService(econ, new NullAnalyticsService(), store, new NullEventBus(),
            Options.Create(new BasketballOptions()), new NullMiniGameSessionGhostHeal());
        await svc.PlaceBetAsync(1, "u", 100, amount: 100, default);
        var result = await svc.ThrowAsync(1, "u", 100, face: 5, default);
        Assert.Equal(BasketballThrowOutcome.Thrown, result.Outcome);
        Assert.Equal(2, result.Multiplier);
        Assert.Equal(200, result.Payout);
    }

    [Fact]
    public async Task ThrowAsync_WithBetFace2_ZeroPayout()
    {
        var econ = new FakeEconomicsService();
        var store = new InMemoryBasketballBetStore();
        var svc = new BasketballService(econ, new NullAnalyticsService(), store, new NullEventBus(),
            Options.Create(new BasketballOptions()), new NullMiniGameSessionGhostHeal());
        await svc.PlaceBetAsync(1, "u", 100, amount: 50, default);
        var result = await svc.ThrowAsync(1, "u", 100, face: 2, default);
        Assert.Equal(0, result.Payout);
        Assert.Empty(econ.Credits);
    }
}

[Collection("MiniGameSession")]
public class BowlingMultiplierTests
{
    public BowlingMultiplierTests() => BotMiniGameSession.DangerousResetAllForTests();

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 0)]
    [InlineData(3, 0)]
    [InlineData(4, 1)]
    [InlineData(5, 2)]
    [InlineData(6, 2)]
    public void Multipliers_CorrectByFace(int face, int expected)
    {
        Assert.Equal(expected, BowlingService.Multipliers[face]);
    }

    [Fact]
    public async Task RollAsync_Strike_CreditsX2()
    {
        var econ = new FakeEconomicsService();
        var store = new InMemoryBowlingBetStore();
        var svc = new BowlingService(econ, new NullAnalyticsService(), store, new NullEventBus(),
            Options.Create(new BowlingOptions()), new NullMiniGameSessionGhostHeal());
        await svc.PlaceBetAsync(1, "u", 100, amount: 50, default);
        var result = await svc.RollAsync(1, "u", 100, face: 6, default);
        Assert.Equal(BowlingRollOutcome.Rolled, result.Outcome);
        Assert.Equal(100, result.Payout);
    }

    [Fact]
    public async Task PlaceBetAsync_DoubleBet_ReturnsAlreadyPending()
    {
        var store = new InMemoryBowlingBetStore();
        var svc = new BowlingService(new FakeEconomicsService(), new NullAnalyticsService(),
            store, new NullEventBus(), Options.Create(new BowlingOptions()), new NullMiniGameSessionGhostHeal());
        await svc.PlaceBetAsync(1, "u", 100, amount: 50, default);
        var result = await svc.PlaceBetAsync(1, "u", 100, amount: 50, default);
        Assert.Equal(BowlingBetError.AlreadyPending, result.Error);
    }

    [Fact]
    public async Task PlaceBetAsync_StaleBasketballSession_GhostHeal_AllowsBet()
    {
        BotMiniGameSession.RegisterPlacedBet(1, 100, MiniGameIds.Basketball);
        var basketStore = new InMemoryBasketballBetStore();
        var heal = new LocalMiniGameSessionGhostHeal(basketball: basketStore);
        var svc = new BowlingService(new FakeEconomicsService(), new NullAnalyticsService(),
            new InMemoryBowlingBetStore(), new NullEventBus(), Options.Create(new BowlingOptions()), heal);
        var result = await svc.PlaceBetAsync(1, "u", 100, amount: 10, default);
        Assert.Equal(BowlingBetError.None, result.Error);
    }
}

[Collection("MiniGameSession")]
public class DartsMultiplierTests
{
    public DartsMultiplierTests()
    {
        BotMiniGameSession.DangerousResetAllForTests();
        DartsDiceRoundBinding.DangerousResetAllForTests();
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 0)]
    [InlineData(3, 0)]
    [InlineData(4, 1)]
    [InlineData(5, 2)]
    [InlineData(6, 2)]
    public void Multipliers_CorrectByFace(int face, int expected)
    {
        Assert.Equal(expected, DartsService.Multipliers[face]);
    }

    [Fact]
    public async Task ThrowAsync_Bullseye_CreditsX2()
    {
        var econ = new FakeEconomicsService();
        var store = new InMemoryDartsRoundStore();
        var svc = new DartsService(econ, new NullAnalyticsService(), store, new NullMiniGameSessionGhostHeal(),
            new NullEventBus(), new DartsRollQueue(), Options.Create(new DartsOptions()));
        var pr = await svc.PlaceBetAsync(1, "u", 100, amount: 100, 1, default);
        Assert.True(await store.TryMarkAwaitingOutcomeAsync(pr.RoundId, 8001, default));
        DartsDiceRoundBinding.Bind(100, 8001, pr.RoundId);
        var result = await svc.ThrowAsync(pr.RoundId, 1, "u", 100, 8001, face: 6, default);
        Assert.Equal(DartsThrowOutcome.Thrown, result.Outcome);
        Assert.Equal(200, result.Payout);
    }

    [Fact]
    public async Task ThrowAsync_NoBet_ReturnsNoBet()
    {
        var svc = new DartsService(new FakeEconomicsService(), new NullAnalyticsService(),
            new InMemoryDartsRoundStore(), new NullMiniGameSessionGhostHeal(), new NullEventBus(), new DartsRollQueue(),
            Options.Create(new DartsOptions()));
        var result = await svc.ThrowAsync(999, 1, "u", 100, 1, face: 6, default);
        Assert.Equal(DartsThrowOutcome.NoBet, result.Outcome);
    }
}

[Collection("MiniGameSession")]
public class FootballMultiplierTests
{
    public FootballMultiplierTests() => BotMiniGameSession.DangerousResetAllForTests();

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 0)]
    [InlineData(3, 0)]
    [InlineData(4, 2)]
    [InlineData(5, 2)]
    public void Multipliers_CorrectByFace(int face, int expected)
    {
        Assert.Equal(expected, FootballService.Multipliers[face]);
    }

    [Fact]
    public async Task ThrowAsync_GoalFace5_CreditsX2()
    {
        var econ = new FakeEconomicsService();
        var store = new InMemoryFootballBetStore();
        var svc = new FootballService(econ, new NullAnalyticsService(), store, new NullEventBus(),
            Options.Create(new FootballOptions()), new NullMiniGameSessionGhostHeal());
        await svc.PlaceBetAsync(1, "u", 100, amount: 100, default);
        var result = await svc.ThrowAsync(1, "u", 100, face: 5, default);
        Assert.Equal(FootballThrowOutcome.Thrown, result.Outcome);
        Assert.Equal(200, result.Payout);
    }

    [Fact]
    public async Task ThrowAsync_NoBet_ReturnsNoBet()
    {
        var svc = new FootballService(new FakeEconomicsService(), new NullAnalyticsService(),
            new InMemoryFootballBetStore(), new NullEventBus(), Options.Create(new FootballOptions()),
            new NullMiniGameSessionGhostHeal());
        var result = await svc.ThrowAsync(1, "u", 100, face: 5, default);
        Assert.Equal(FootballThrowOutcome.NoBet, result.Outcome);
    }
}

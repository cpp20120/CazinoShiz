using BotFramework.Sdk;
using Games.Basketball;
using Games.Bowling;
using Games.Darts;
using Games.Football;
using Games.DiceCube;
using Microsoft.Extensions.Caching.Memory;
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
            new FakeRuntimeTuning(), new NullMiniGameSessionGhostHeal(), new NullTelegramDiceDailyRollLimiter());
        var result = await svc.PlaceBetAsync(1, "u", 100, amount: 0, default);
        Assert.Equal(CubeBetError.InvalidAmount, result.Error);
    }

    [Fact]
    public async Task PlaceBetAsync_InsufficientBalance_ReturnsFail()
    {
        var econ = new FakeEconomicsService { StartingBalance = 10 };
        var svc = new DiceCubeService(econ, new NullAnalyticsService(),
            new InMemoryDiceCubeBetStore(), new NullEventBus(), new MemoryCache(new MemoryCacheOptions()),
            new FakeRuntimeTuning(), new NullMiniGameSessionGhostHeal(), new NullTelegramDiceDailyRollLimiter());
        var result = await svc.PlaceBetAsync(1, "u", 100, amount: 100, default);
        Assert.Equal(CubeBetError.NotEnoughCoins, result.Error);
    }

    [Fact]
    public async Task PlaceBetAsync_DoubleBet_ReturnsAlreadyPending()
    {
        var store = new InMemoryDiceCubeBetStore();
        var svc = new DiceCubeService(new FakeEconomicsService(), new NullAnalyticsService(),
            store, new NullEventBus(), new MemoryCache(new MemoryCacheOptions()),
            new FakeRuntimeTuning(), new NullMiniGameSessionGhostHeal(), new NullTelegramDiceDailyRollLimiter());
        await svc.PlaceBetAsync(1, "u", 100, amount: 50, default);
        var result = await svc.PlaceBetAsync(1, "u", 100, amount: 50, default);
        Assert.Equal(CubeBetError.AlreadyPending, result.Error);
    }

    [Fact]
    public async Task RollAsync_NoBet_ReturnsNoBet()
    {
        var svc = new DiceCubeService(new FakeEconomicsService(), new NullAnalyticsService(),
            new InMemoryDiceCubeBetStore(), new NullEventBus(), new MemoryCache(new MemoryCacheOptions()),
            new FakeRuntimeTuning(), new NullMiniGameSessionGhostHeal(), new NullTelegramDiceDailyRollLimiter());
        var result = await svc.RollAsync(1, "u", 100, face: 6, default);
        Assert.Equal(CubeRollOutcome.NoBet, result.Outcome);
    }

    [Fact]
    public async Task RollAsync_WithBetFace6_CreditsX2()
    {
        var econ = new FakeEconomicsService();
        var store = new InMemoryDiceCubeBetStore();
        var svc = new DiceCubeService(econ, new NullAnalyticsService(), store, new NullEventBus(), new MemoryCache(new MemoryCacheOptions()),
            new FakeRuntimeTuning(), new NullMiniGameSessionGhostHeal(), new NullTelegramDiceDailyRollLimiter());
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
            new FakeRuntimeTuning(), new NullMiniGameSessionGhostHeal(), new NullTelegramDiceDailyRollLimiter());
        await svc.PlaceBetAsync(1, "u", 100, amount: 100, default);
        var result = await svc.RollAsync(1, "u", 100, face: 1, default);
        Assert.Equal(CubeRollOutcome.Rolled, result.Outcome);
        Assert.Equal(0, result.Payout);
        Assert.Empty(econ.Credits);
    }
}

using BotFramework.Sdk;
using Games.Basketball;
using Games.Bowling;
using Games.Darts;
using Games.Football;
using Games.DiceCube;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace CasinoShiz.Tests;
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
            new InMemoryBasketballBetStore(), new NullEventBus(), new FakeRuntimeTuning(),
            new NullMiniGameSessionGhostHeal(), new NullTelegramDiceDailyRollLimiter());
        var result = await svc.PlaceBetAsync(1, "u", 100, amount: -5, default);
        Assert.Equal(BasketballBetError.InvalidAmount, result.Error);
    }

    [Fact]
    public async Task ThrowAsync_WithBetFace5_CreditsX2()
    {
        var econ = new FakeEconomicsService();
        var store = new InMemoryBasketballBetStore();
        var svc = new BasketballService(econ, new NullAnalyticsService(), store, new NullEventBus(),
            new FakeRuntimeTuning(), new NullMiniGameSessionGhostHeal(), new NullTelegramDiceDailyRollLimiter());
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
            new FakeRuntimeTuning(), new NullMiniGameSessionGhostHeal(), new NullTelegramDiceDailyRollLimiter());
        await svc.PlaceBetAsync(1, "u", 100, amount: 50, default);
        var result = await svc.ThrowAsync(1, "u", 100, face: 2, default);
        Assert.Equal(0, result.Payout);
        Assert.Empty(econ.Credits);
    }
}

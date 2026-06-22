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
            new FakeRuntimeTuning(), new NullMiniGameSessionGhostHeal(), new NullTelegramDiceDailyRollLimiter());
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
            store, new NullEventBus(), new FakeRuntimeTuning(), new NullMiniGameSessionGhostHeal(), new NullTelegramDiceDailyRollLimiter());
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
            new InMemoryBowlingBetStore(), new NullEventBus(), new FakeRuntimeTuning(), heal, new NullTelegramDiceDailyRollLimiter());
        var result = await svc.PlaceBetAsync(1, "u", 100, amount: 10, default);
        Assert.Equal(BowlingBetError.None, result.Error);
    }
}

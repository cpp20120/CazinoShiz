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
            new FakeRuntimeTuning(), new NullMiniGameSessionGhostHeal(), new NullTelegramDiceDailyRollLimiter());
        await svc.PlaceBetAsync(1, "u", 100, amount: 100, default);
        var result = await svc.ThrowAsync(1, "u", 100, face: 5, default);
        Assert.Equal(FootballThrowOutcome.Thrown, result.Outcome);
        Assert.Equal(200, result.Payout);
    }

    [Fact]
    public async Task ThrowAsync_NoBet_ReturnsNoBet()
    {
        var svc = new FootballService(new FakeEconomicsService(), new NullAnalyticsService(),
            new InMemoryFootballBetStore(), new NullEventBus(), new FakeRuntimeTuning(),
            new NullMiniGameSessionGhostHeal(), new NullTelegramDiceDailyRollLimiter());
        var result = await svc.ThrowAsync(1, "u", 100, face: 5, default);
        Assert.Equal(FootballThrowOutcome.NoBet, result.Outcome);
    }
}

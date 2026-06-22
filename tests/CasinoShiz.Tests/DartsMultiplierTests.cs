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
            new NullEventBus(), new DartsRollQueue(), new FakeRuntimeTuning(), new NullTelegramDiceDailyRollLimiter());
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
            new FakeRuntimeTuning(), new NullTelegramDiceDailyRollLimiter());
        var result = await svc.ThrowAsync(999, 1, "u", 100, 1, face: 6, default);
        Assert.Equal(DartsThrowOutcome.NoBet, result.Outcome);
    }
}

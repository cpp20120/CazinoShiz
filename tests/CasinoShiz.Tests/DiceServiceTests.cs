using BotFramework.Host;
using BotFramework.Sdk;
using Games.Basketball;
using Games.Dice;
using Xunit;

namespace CasinoShiz.Tests;

// Telegram slot-machine dice values are base-4 packed triples:
//   value 1  → [0,0,0] = three bars  → prize 21
//   value 22 → [1,1,1] = three cherries → prize 23
//   value 43 → [2,2,2] = three lemons → prize 30
//   value 64 → [3,3,3] = three sevens → prize 77
public class DiceServiceTests
{
    private static DiceService MakeService(
        FakeEconomicsService? economics = null,
        int cost = 7,
        ITelegramDiceDailyRollLimiter? limiter = null) =>
        new(
            economics ?? new FakeEconomicsService(),
            new NullAnalyticsService(),
            new NullDiceHistoryStore(),
            new NullEventBus(),
            limiter ?? new NullTelegramDiceDailyRollLimiter(),
            new FakeRuntimeTuning { Dice = new DiceOptions { Cost = cost } });

    [Fact]
    public async Task PlayAsync_ForwardedMessage_ReturnsForwarded()
    {
        var svc = MakeService();
        var result = await svc.PlayAsync(1, "u", 64, 100, isForwarded: true, default);
        Assert.Equal(DiceOutcome.Forwarded, result.Outcome);
    }

    [Fact]
    public async Task PlayAsync_ForwardedMessage_NoDebit()
    {
        var econ = new FakeEconomicsService();
        var svc = MakeService(econ);
        await svc.PlayAsync(1, "u", 64, 100, isForwarded: true, default);
        Assert.Empty(econ.Debits);
    }

    [Fact]
    public async Task PlayAsync_InsufficientBalance_ReturnsNotEnoughCoins()
    {
        var econ = new FakeEconomicsService { StartingBalance = 0 };
        var svc = MakeService(econ);
        var result = await svc.PlayAsync(1, "u", 64, 100, isForwarded: false, default);
        Assert.Equal(DiceOutcome.NotEnoughCoins, result.Outcome);
    }

    [Fact]
    public async Task PlayAsync_DailyRollLimit_ReturnsLimitExceeded()
    {
        var svc = MakeService(limiter: new RejectingTelegramDiceDailyRollLimiter());
        var result = await svc.PlayAsync(1, "u", 64, 100, isForwarded: false, default);
        Assert.Equal(DiceOutcome.DailyRollLimitExceeded, result.Outcome);
        Assert.Equal(3, result.DailyDiceUsed);
        Assert.Equal(10, result.DailyDiceLimit);
    }

    [Fact]
    public async Task DailyRollLimit_IsTrackedIndependentlyPerGame()
    {
        BotMiniGameSession.DangerousResetAllForTests();
        try
        {
            var limiter = new GameScopedTelegramDiceDailyRollLimiter(maxRollsPerGame: 1);
            var economics = new FakeEconomicsService();
            var dice = MakeService(economics, limiter: limiter);
            var basketball = new BasketballService(
                economics,
                new NullAnalyticsService(),
                new InMemoryBasketballBetStore(),
                new NullEventBus(),
                new FakeRuntimeTuning(),
                new NullMiniGameSessionGhostHeal(),
                limiter);

            var firstDice = await dice.PlayAsync(1, "u", 64, 100, isForwarded: false, default);
            var secondDice = await dice.PlayAsync(1, "u", 64, 100, isForwarded: false, default);
            var basketballBet = await basketball.PlaceBetAsync(1, "u", 100, 10, default);

            Assert.Equal(DiceOutcome.Played, firstDice.Outcome);
            Assert.Equal(DiceOutcome.DailyRollLimitExceeded, secondDice.Outcome);
            Assert.Equal(BasketballBetError.None, basketballBet.Error);
        }
        finally
        {
            BotMiniGameSession.DangerousResetAllForTests();
        }
    }

    [Fact]
    public async Task PlayAsync_InsufficientBalance_RefundsConsumedRoll()
    {
        var recording = new RecordingTelegramDiceDailyRollLimiter();
        var econ = new FakeEconomicsService { StartingBalance = 0 };
        var svc = MakeService(econ, limiter: recording);
        await svc.PlayAsync(1, "u", 64, 100, isForwarded: false, default);
        Assert.Equal(1, recording.RefundCount);
    }

    [Fact]
    public async Task PlayAsync_TripleSeven_Returns77Prize()
    {
        var svc = MakeService();
        // value 64 decodes to [3,3,3] = three sevens
        var result = await svc.PlayAsync(1, "u", 64, 100, isForwarded: false, default);
        Assert.Equal(DiceOutcome.Played, result.Outcome);
        Assert.Equal(77, result.Prize);
    }

    [Fact]
    public async Task PlayAsync_TripleBar_Returns21Prize()
    {
        var svc = MakeService();
        // value 1 decodes to [0,0,0] = three bars
        var result = await svc.PlayAsync(1, "u", 1, 100, isForwarded: false, default);
        Assert.Equal(DiceOutcome.Played, result.Outcome);
        Assert.Equal(21, result.Prize);
    }

    [Fact]
    public async Task PlayAsync_TripleCherry_Returns23Prize()
    {
        var svc = MakeService();
        // value 22 decodes to [1,1,1] = three cherries
        var result = await svc.PlayAsync(1, "u", 22, 100, isForwarded: false, default);
        Assert.Equal(DiceOutcome.Played, result.Outcome);
        Assert.Equal(23, result.Prize);
    }

    [Fact]
    public async Task PlayAsync_TripleLemon_Returns30Prize()
    {
        var svc = MakeService();
        // value 43 decodes to [2,2,2] = three lemons
        var result = await svc.PlayAsync(1, "u", 43, 100, isForwarded: false, default);
        Assert.Equal(DiceOutcome.Played, result.Outcome);
        Assert.Equal(30, result.Prize);
    }

    [Fact]
    public async Task PlayAsync_ValidRoll_DebitsStakeAndGas()
    {
        var econ = new FakeEconomicsService();
        var svc = MakeService(econ, cost: 7);
        await svc.PlayAsync(1, "u", 64, 100, isForwarded: false, default);
        Assert.Single(econ.Debits);
        // loss = cost + gas; gas for cost=7 (< 10) is at least 1
        Assert.True(econ.Debits[0].Amount >= 8);
    }

    [Fact]
    public async Task PlayAsync_WinningRoll_CreditsPrize()
    {
        var econ = new FakeEconomicsService();
        var svc = MakeService(econ);
        // three sevens always wins
        await svc.PlayAsync(1, "u", 64, 100, isForwarded: false, default);
        Assert.Single(econ.Credits);
        Assert.Equal(77, econ.Credits[0].Amount);
    }
}

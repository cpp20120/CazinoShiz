using BotFramework.Host;
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
        IDiceBetStore? bets = null,
        FakeEconomicsService? economics = null,
        int cost = 7,
        ITelegramDiceDailyRollLimiter? limiter = null) =>
        new(
            economics ?? new FakeEconomicsService(),
            new NullAnalyticsService(),
            bets ?? new InMemoryDiceBetStore(),
            new NullDiceHistoryStore(),
            new NullEventBus(),
            limiter ?? new NullTelegramDiceDailyRollLimiter(),
            new FakeRuntimeTuning { Dice = new DiceOptions { Cost = cost } });

    [Fact]
    public async Task PlaceBetAsync_ForwardedMessage_ReturnsForwarded()
    {
        var svc = MakeService();
        var result = await svc.PlaceBetAsync(1, "u", 64, 100, isForwarded: true, default);
        Assert.Equal(DiceOutcome.Forwarded, result.Outcome);
    }

    [Fact]
    public async Task PlaceBetAsync_ForwardedMessage_NoDebit()
    {
        var econ = new FakeEconomicsService();
        var svc = MakeService(economics: econ);
        await svc.PlaceBetAsync(1, "u", 64, 100, isForwarded: true, default);
        Assert.Empty(econ.Debits);
    }

    [Fact]
    public async Task PlaceBetAsync_InsufficientBalance_ReturnsNotEnoughCoins()
    {
        var econ = new FakeEconomicsService { StartingBalance = 0 };
        var svc = MakeService(economics: econ);
        var result = await svc.PlaceBetAsync(1, "u", 64, 100, isForwarded: false, default);
        Assert.Equal(DiceOutcome.NotEnoughCoins, result.Outcome);
    }

    [Fact]
    public async Task PlaceBetAsync_DailyRollLimit_ReturnsLimitExceeded()
    {
        var svc = MakeService(limiter: new RejectingTelegramDiceDailyRollLimiter());
        var result = await svc.PlaceBetAsync(1, "u", 64, 100, isForwarded: false, default);
        Assert.Equal(DiceOutcome.DailyRollLimitExceeded, result.Outcome);
        Assert.Equal(3, result.DailyDiceUsed);
        Assert.Equal(10, result.DailyDiceLimit);
    }

    [Fact]
    public async Task PlaceBetAsync_InsufficientBalance_RefundsConsumedRoll()
    {
        var recording = new RecordingTelegramDiceDailyRollLimiter();
        var econ = new FakeEconomicsService { StartingBalance = 0 };
        var svc = MakeService(economics: econ, limiter: recording);
        await svc.PlaceBetAsync(1, "u", 64, 100, isForwarded: false, default);
        Assert.Equal(1, recording.RefundCount);
    }

    [Fact]
    public async Task PlaceBetAsync_ValidRoll_DebitsStakeAndGas()
    {
        var econ = new FakeEconomicsService();
        var svc = MakeService(economics: econ, cost: 7);
        await svc.PlaceBetAsync(1, "u", 64, 100, isForwarded: false, default);
        Assert.Single(econ.Debits);
        // loss = cost + gas; gas for cost=7 (< 10) is at least 1
        Assert.True(econ.Debits[0].Amount >= 8);
    }

    [Fact]
    public async Task PlaceBetAsync_ValidRoll_ReturnsBetPlaced()
    {
        var svc = MakeService();
        var result = await svc.PlaceBetAsync(1, "u", 64, 100, isForwarded: false, default);
        Assert.Equal(DiceOutcome.BetPlaced, result.Outcome);
    }

    [Fact]
    public async Task ResolveBetAsync_TripleSeven_Returns77Prize()
    {
        var bets = new InMemoryDiceBetStore();
        var svc = MakeService(bets: bets);

        // Place bet with value 64 (three sevens)
        await svc.PlaceBetAsync(1, "u", 64, 100, isForwarded: false, default);

        // Resolve it
        var result = await svc.ResolveBetAsync(1, "u", 100, default);
        Assert.Equal(DiceOutcome.Played, result.Outcome);
        Assert.Equal(77, result.Prize);
    }

    [Fact]
    public async Task ResolveBetAsync_TripleBar_Returns21Prize()
    {
        var bets = new InMemoryDiceBetStore();
        var svc = MakeService(bets: bets);

        // Place bet with value 1 (three bars)
        await svc.PlaceBetAsync(1, "u", 1, 100, isForwarded: false, default);

        // Resolve it
        var result = await svc.ResolveBetAsync(1, "u", 100, default);
        Assert.Equal(DiceOutcome.Played, result.Outcome);
        Assert.Equal(21, result.Prize);
    }

    [Fact]
    public async Task ResolveBetAsync_TripleCherry_Returns23Prize()
    {
        var bets = new InMemoryDiceBetStore();
        var svc = MakeService(bets: bets);

        // Place bet with value 22 (three cherries)
        await svc.PlaceBetAsync(1, "u", 22, 100, isForwarded: false, default);

        // Resolve it
        var result = await svc.ResolveBetAsync(1, "u", 100, default);
        Assert.Equal(DiceOutcome.Played, result.Outcome);
        Assert.Equal(23, result.Prize);
    }

    [Fact]
    public async Task ResolveBetAsync_TripleLemon_Returns30Prize()
    {
        var bets = new InMemoryDiceBetStore();
        var svc = MakeService(bets: bets);

        // Place bet with value 43 (three lemons)
        await svc.PlaceBetAsync(1, "u", 43, 100, isForwarded: false, default);

        // Resolve it
        var result = await svc.ResolveBetAsync(1, "u", 100, default);
        Assert.Equal(DiceOutcome.Played, result.Outcome);
        Assert.Equal(30, result.Prize);
    }

    [Fact]
    public async Task ResolveBetAsync_WinningRoll_CreditsPrize()
    {
        var econ = new FakeEconomicsService();
        var bets = new InMemoryDiceBetStore();
        var svc = MakeService(bets: bets, economics: econ);

        // Place bet with value 64 (three sevens, always wins)
        await svc.PlaceBetAsync(1, "u", 64, 100, isForwarded: false, default);

        // Resolve it
        await svc.ResolveBetAsync(1, "u", 100, default);

        // Should have credit for the prize
        Assert.Single(econ.Credits);
        Assert.Equal(77, econ.Credits[0].Amount);
    }

    [Fact]
    public async Task ResolveBetAsync_NoPendingBet_ReturnsNoPendingBet()
    {
        var svc = MakeService();
        var result = await svc.ResolveBetAsync(1, "u", 100, default);
        Assert.Equal(DiceOutcome.NoPendingBet, result.Outcome);
    }

    [Fact]
    public async Task AbortBetAfterSendDiceFailedAsync_RefundsDebit()
    {
        var econ = new FakeEconomicsService();
        var bets = new InMemoryDiceBetStore();
        var svc = MakeService(bets: bets, economics: econ);

        // Place bet
        await svc.PlaceBetAsync(1, "u", 64, 100, isForwarded: false, default);
        var debits = econ.Debits.Count;

        // Abort it
        await svc.AbortBetAfterSendDiceFailedAsync(1, 100, default);

        // Should have a credit (refund) after the debits
        Assert.True(econ.Credits.Count >= 1);
        Assert.Equal(econ.Debits[0].Amount, econ.Credits[^1].Amount);
    }
}

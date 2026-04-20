using Games.Darts;
using Xunit;

namespace CasinoShiz.Tests;

public class DartsServiceTests
{
    private static DartsService MakeService(
        FakeEconomicsService? economics = null,
        InMemoryDartsBetStore? bets = null) =>
        new(
            economics ?? new FakeEconomicsService(),
            new NullAnalyticsService(),
            bets ?? new InMemoryDartsBetStore(),
            new NullEventBus());

    [Fact]
    public async Task PlaceBetAsync_ZeroAmount_ReturnsInvalidAmount()
    {
        var svc = MakeService();
        var result = await svc.PlaceBetAsync(1, "u", 100, 0, default);
        Assert.Equal(DartsBetError.InvalidAmount, result.Error);
    }

    [Fact]
    public async Task PlaceBetAsync_NegativeAmount_ReturnsInvalidAmount()
    {
        var svc = MakeService();
        var result = await svc.PlaceBetAsync(1, "u", 100, -50, default);
        Assert.Equal(DartsBetError.InvalidAmount, result.Error);
    }

    [Fact]
    public async Task PlaceBetAsync_InsufficientBalance_ReturnsNotEnoughCoins()
    {
        var econ = new FakeEconomicsService { StartingBalance = 10 };
        var svc = MakeService(economics: econ);
        var result = await svc.PlaceBetAsync(1, "u", 100, 100, default);
        Assert.Equal(DartsBetError.NotEnoughCoins, result.Error);
    }

    [Fact]
    public async Task PlaceBetAsync_InsufficientBalance_ReturnsCurrentBalance()
    {
        var econ = new FakeEconomicsService { StartingBalance = 10 };
        var svc = MakeService(economics: econ);
        var result = await svc.PlaceBetAsync(1, "u", 100, 100, default);
        Assert.Equal(10, result.Balance);
    }

    [Fact]
    public async Task PlaceBetAsync_Success_ReturnsNoneError()
    {
        var svc = MakeService();
        var result = await svc.PlaceBetAsync(1, "u", 100, 50, default);
        Assert.Equal(DartsBetError.None, result.Error);
    }

    [Fact]
    public async Task PlaceBetAsync_Success_DebitsAmount()
    {
        var econ = new FakeEconomicsService();
        var svc = MakeService(economics: econ);
        await svc.PlaceBetAsync(1, "u", 100, 50, default);
        Assert.Single(econ.Debits);
        Assert.Equal(50, econ.Debits[0].Amount);
    }

    [Fact]
    public async Task PlaceBetAsync_Success_BalanceReducedByBet()
    {
        var econ = new FakeEconomicsService { StartingBalance = 200 };
        var svc = MakeService(economics: econ);
        var result = await svc.PlaceBetAsync(1, "u", 100, 50, default);
        Assert.Equal(150, result.Balance);
    }

    [Fact]
    public async Task PlaceBetAsync_Success_AmountMatchesBet()
    {
        var svc = MakeService();
        var result = await svc.PlaceBetAsync(1, "u", 100, 75, default);
        Assert.Equal(75, result.Amount);
    }

    [Fact]
    public async Task PlaceBetAsync_AlreadyPending_ReturnsAlreadyPending()
    {
        var bets = new InMemoryDartsBetStore();
        var svc = MakeService(bets: bets);
        await svc.PlaceBetAsync(1, "u", 100, 50, default);
        var result = await svc.PlaceBetAsync(1, "u", 100, 30, default);
        Assert.Equal(DartsBetError.AlreadyPending, result.Error);
    }

    [Fact]
    public async Task PlaceBetAsync_AlreadyPending_ReturnsPendingAmount()
    {
        var bets = new InMemoryDartsBetStore();
        var svc = MakeService(bets: bets);
        await svc.PlaceBetAsync(1, "u", 100, 50, default);
        var result = await svc.PlaceBetAsync(1, "u", 100, 30, default);
        Assert.Equal(50, result.PendingAmount);
    }

    [Fact]
    public async Task PlaceBetAsync_SameUserDifferentChats_BothSucceed()
    {
        var econ = new FakeEconomicsService { StartingBalance = 1_000 };
        var bets = new InMemoryDartsBetStore();
        var svc = MakeService(economics: econ, bets: bets);
        var r1 = await svc.PlaceBetAsync(1, "u", chatId: 100, 50, default);
        var r2 = await svc.PlaceBetAsync(1, "u", chatId: 200, 30, default);
        Assert.Equal(DartsBetError.None, r1.Error);
        Assert.Equal(DartsBetError.None, r2.Error);
    }

    [Fact]
    public async Task ThrowAsync_NoBet_ReturnsNoBet()
    {
        var svc = MakeService();
        var result = await svc.ThrowAsync(1, "u", 100, 4, default);
        Assert.Equal(DartsThrowOutcome.NoBet, result.Outcome);
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 0)]
    [InlineData(3, 0)]
    [InlineData(4, 2)]
    [InlineData(5, 3)]
    [InlineData(6, 6)]
    public async Task ThrowAsync_ReturnsCorrectMultiplier(int face, int expectedMultiplier)
    {
        var bets = new InMemoryDartsBetStore();
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
    [InlineData(5, 150)]
    [InlineData(6, 300)]
    public async Task ThrowAsync_ReturnsCorrectPayout(int face, int expectedPayout)
    {
        var bets = new InMemoryDartsBetStore();
        var svc = MakeService(bets: bets);
        await svc.PlaceBetAsync(1, "u", 100, 50, default);
        var result = await svc.ThrowAsync(1, "u", 100, face, default);
        Assert.Equal(expectedPayout, result.Payout);
    }

    [Fact]
    public async Task ThrowAsync_Bullseye_Pays6x_NotSameAsDiceCube()
    {
        // Darts bullseye (face 6) pays 6x; DiceCube face 6 pays 5x
        Assert.Equal(6, DartsService.Multipliers[6]);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public async Task ThrowAsync_WinningFace_CreditsPayout(int face)
    {
        var econ = new FakeEconomicsService();
        var bets = new InMemoryDartsBetStore();
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
        var bets = new InMemoryDartsBetStore();
        var svc = MakeService(economics: econ, bets: bets);
        await svc.PlaceBetAsync(1, "u", 100, 50, default);
        await svc.ThrowAsync(1, "u", 100, face, default);
        Assert.Empty(econ.Credits);
    }

    [Fact]
    public async Task ThrowAsync_AfterThrow_BetIsDeleted()
    {
        var bets = new InMemoryDartsBetStore();
        var svc = MakeService(bets: bets);
        await svc.PlaceBetAsync(1, "u", 100, 50, default);
        await svc.ThrowAsync(1, "u", 100, 4, default);
        var second = await svc.ThrowAsync(1, "u", 100, 4, default);
        Assert.Equal(DartsThrowOutcome.NoBet, second.Outcome);
    }

    [Fact]
    public async Task ThrowAsync_UnknownFace_NoPayout()
    {
        var bets = new InMemoryDartsBetStore();
        var svc = MakeService(bets: bets);
        await svc.PlaceBetAsync(1, "u", 100, 50, default);
        var result = await svc.ThrowAsync(1, "u", 100, 99, default);
        Assert.Equal(0, result.Payout);
        Assert.Equal(0, result.Multiplier);
    }

    [Fact]
    public async Task ThrowAsync_Success_ReturnsThrowOutcome()
    {
        var bets = new InMemoryDartsBetStore();
        var svc = MakeService(bets: bets);
        await svc.PlaceBetAsync(1, "u", 100, 50, default);
        var result = await svc.ThrowAsync(1, "u", 100, 4, default);
        Assert.Equal(DartsThrowOutcome.Thrown, result.Outcome);
    }

    [Fact]
    public async Task ThrowAsync_Success_ReturnsFace()
    {
        var bets = new InMemoryDartsBetStore();
        var svc = MakeService(bets: bets);
        await svc.PlaceBetAsync(1, "u", 100, 50, default);
        var result = await svc.ThrowAsync(1, "u", 100, 6, default);
        Assert.Equal(6, result.Face);
    }

    [Fact]
    public async Task ThrowAsync_Success_ReturnsBetAmount()
    {
        var bets = new InMemoryDartsBetStore();
        var svc = MakeService(bets: bets);
        await svc.PlaceBetAsync(1, "u", 100, 50, default);
        var result = await svc.ThrowAsync(1, "u", 100, 4, default);
        Assert.Equal(50, result.Bet);
    }

    [Fact]
    public async Task ThrowAsync_PublishesThrowCompletedEvent()
    {
        var bus = new NullEventBus();
        var bets = new InMemoryDartsBetStore();
        var svc = new DartsService(new FakeEconomicsService(), new NullAnalyticsService(), bets, bus);
        await svc.PlaceBetAsync(1, "u", 100, 50, default);
        await svc.ThrowAsync(1, "u", 100, 4, default);
        Assert.Single(bus.Published);
        Assert.IsType<DartsThrowCompleted>(bus.Published[0]);
    }

    [Fact]
    public async Task Multipliers_ContainsAllSixFaces()
    {
        for (var face = 1; face <= 6; face++)
            Assert.True(DartsService.Multipliers.ContainsKey(face));
    }
}

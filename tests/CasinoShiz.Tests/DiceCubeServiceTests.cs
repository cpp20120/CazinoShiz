using BotFramework.Sdk;
using Games.Darts;
using Games.DiceCube;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Xunit;

namespace CasinoShiz.Tests;

[Collection("MiniGameSession")]
public class DiceCubeServiceTests
{
    public DiceCubeServiceTests()
    {
        BotMiniGameSession.DangerousResetAllForTests();
        DartsDiceRoundBinding.DangerousResetAllForTests();
    }

    private static IMemoryCache NewCache() => new MemoryCache(new MemoryCacheOptions());

    private static DiceCubeService MakeService(
        FakeEconomicsService? economics = null,
        InMemoryDiceCubeBetStore? bets = null,
        IMemoryCache? cache = null,
        DiceCubeOptions? o = null) =>
        new(
            economics ?? new FakeEconomicsService(),
            new NullAnalyticsService(),
            bets ?? new InMemoryDiceCubeBetStore(),
            new NullEventBus(),
            cache ?? NewCache(),
            Options.Create(o ?? new DiceCubeOptions()));

    [Fact]
    public async Task PlaceBetAsync_PendingOtherMiniGame_ReturnsBusyOtherGame()
    {
        var svc = MakeService();
        var darts = new DartsService(
            new FakeEconomicsService(),
            new NullAnalyticsService(),
            new InMemoryDartsRoundStore(),
            new InMemoryDiceCubeBetStore(),
            new NullEventBus(),
            new DartsRollQueue(),
            Options.Create(new DartsOptions()));
        await darts.PlaceBetAsync(1, "u", 100, 50, 1, default);
        var result = await svc.PlaceBetAsync(1, "u", 100, 30, default);
        Assert.Equal(CubeBetError.BusyOtherGame, result.Error);
        Assert.Equal(MiniGameIds.Darts, result.BlockingGameId);
    }

    [Fact]
    public async Task PlaceBetAsync_AfterOtherMiniGameResolves_AllowsBet()
    {
        var econ = new FakeEconomicsService();
        var svc = MakeService(economics: econ);
        var dRounds = new InMemoryDartsRoundStore();
        var darts = new DartsService(
            econ,
            new NullAnalyticsService(),
            dRounds,
            new InMemoryDiceCubeBetStore(),
            new NullEventBus(),
            new DartsRollQueue(),
            Options.Create(new DartsOptions()));
        var dp = await darts.PlaceBetAsync(1, "u", 100, 50, 1, default);
        Assert.True(await dRounds.TryMarkAwaitingOutcomeAsync(dp.RoundId, 4242, default));
        DartsDiceRoundBinding.Bind(100, 4242, dp.RoundId);
        await darts.ThrowAsync(dp.RoundId, 1, "u", 100, 4242, 4, default);
        var result = await svc.PlaceBetAsync(1, "u", 100, 30, default);
        Assert.Equal(CubeBetError.None, result.Error);
    }

    [Fact]
    public async Task PlaceBetAsync_NoPendingRow_ClearsStaleDiceCubeSession()
    {
        BotMiniGameSession.RegisterPlacedBet(1, 100, MiniGameIds.DiceCube);
        var svc = MakeService();
        var result = await svc.PlaceBetAsync(1, "u", 100, 30, default);
        Assert.Equal(CubeBetError.None, result.Error);
    }

    [Fact]
    public async Task PlaceBetAsync_ZeroAmount_ReturnsInvalidAmount()
    {
        var svc = MakeService();
        var result = await svc.PlaceBetAsync(1, "u", 100, 0, default);
        Assert.Equal(CubeBetError.InvalidAmount, result.Error);
    }

    [Fact]
    public async Task PlaceBetAsync_NegativeAmount_ReturnsInvalidAmount()
    {
        var svc = MakeService();
        var result = await svc.PlaceBetAsync(1, "u", 100, -50, default);
        Assert.Equal(CubeBetError.InvalidAmount, result.Error);
    }

    [Fact]
    public async Task PlaceBetAsync_InsufficientBalance_ReturnsNotEnoughCoins()
    {
        var econ = new FakeEconomicsService { StartingBalance = 10 };
        var svc = MakeService(economics: econ);
        var result = await svc.PlaceBetAsync(1, "u", 100, 100, default);
        Assert.Equal(CubeBetError.NotEnoughCoins, result.Error);
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
        Assert.Equal(CubeBetError.None, result.Error);
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
        var bets = new InMemoryDiceCubeBetStore();
        var svc = MakeService(bets: bets);
        await svc.PlaceBetAsync(1, "u", 100, 50, default);
        var result = await svc.PlaceBetAsync(1, "u", 100, 30, default);
        Assert.Equal(CubeBetError.AlreadyPending, result.Error);
    }

    [Fact]
    public async Task PlaceBetAsync_AlreadyPending_ReturnsPendingAmount()
    {
        var bets = new InMemoryDiceCubeBetStore();
        var svc = MakeService(bets: bets);
        await svc.PlaceBetAsync(1, "u", 100, 50, default);
        var result = await svc.PlaceBetAsync(1, "u", 100, 30, default);
        Assert.Equal(50, result.PendingAmount);
    }

    [Fact]
    public async Task AbortPendingBetAfterSendDiceFailedAsync_RefundsAndAllowsNewBet()
    {
        var econ = new FakeEconomicsService { StartingBalance = 200 };
        var bets = new InMemoryDiceCubeBetStore();
        var svc = MakeService(economics: econ, bets: bets);
        await svc.PlaceBetAsync(1, "u", 100, 50, default);
        await svc.AbortPendingBetAfterSendDiceFailedAsync(1, 100, default);
        Assert.Contains(econ.Credits, c => c is { Amount: 50, Reason: "dicecube.bot_dice.failed" });
        var again = await svc.PlaceBetAsync(1, "u", 100, 40, default);
        Assert.Equal(CubeBetError.None, again.Error);
    }

    [Fact]
    public async Task PlaceBetAsync_SameUserDifferentChats_BothSucceed()
    {
        var econ = new FakeEconomicsService { StartingBalance = 1_000 };
        var bets = new InMemoryDiceCubeBetStore();
        var svc = MakeService(economics: econ, bets: bets);
        var r1 = await svc.PlaceBetAsync(1, "u", chatId: 100, 50, default);
        var r2 = await svc.PlaceBetAsync(1, "u", chatId: 200, 30, default);
        Assert.Equal(CubeBetError.None, r1.Error);
        Assert.Equal(CubeBetError.None, r2.Error);
    }

    [Fact]
    public async Task RollAsync_NoBet_ReturnsNoBet()
    {
        var svc = MakeService();
        var result = await svc.RollAsync(1, "u", 100, 4, default);
        Assert.Equal(CubeRollOutcome.NoBet, result.Outcome);
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 0)]
    [InlineData(3, 0)]
    [InlineData(4, 1)]
    [InlineData(5, 2)]
    [InlineData(6, 2)]
    public async Task RollAsync_ReturnsCorrectMultiplier(int face, int expectedMultiplier)
    {
        var bets = new InMemoryDiceCubeBetStore();
        var svc = MakeService(bets: bets);
        await svc.PlaceBetAsync(1, "u", 100, 50, default);
        var result = await svc.RollAsync(1, "u", 100, face, default);
        Assert.Equal(expectedMultiplier, result.Multiplier);
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 0)]
    [InlineData(3, 0)]
    [InlineData(4, 50)]
    [InlineData(5, 100)]
    [InlineData(6, 100)]
    public async Task RollAsync_ReturnsCorrectPayout(int face, int expectedPayout)
    {
        var bets = new InMemoryDiceCubeBetStore();
        var svc = MakeService(bets: bets);
        await svc.PlaceBetAsync(1, "u", 100, 50, default);
        var result = await svc.RollAsync(1, "u", 100, face, default);
        Assert.Equal(expectedPayout, result.Payout);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public async Task RollAsync_WinningFace_CreditsPayout(int face)
    {
        var econ = new FakeEconomicsService();
        var bets = new InMemoryDiceCubeBetStore();
        var svc = MakeService(economics: econ, bets: bets);
        await svc.PlaceBetAsync(1, "u", 100, 50, default);
        await svc.RollAsync(1, "u", 100, face, default);
        Assert.Single(econ.Credits);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task RollAsync_LosingFace_NoCredit(int face)
    {
        var econ = new FakeEconomicsService();
        var bets = new InMemoryDiceCubeBetStore();
        var svc = MakeService(economics: econ, bets: bets);
        await svc.PlaceBetAsync(1, "u", 100, 50, default);
        await svc.RollAsync(1, "u", 100, face, default);
        Assert.Empty(econ.Credits);
    }

    [Fact]
    public async Task RollAsync_AfterRoll_BetIsDeleted()
    {
        var bets = new InMemoryDiceCubeBetStore();
        var svc = MakeService(bets: bets);
        await svc.PlaceBetAsync(1, "u", 100, 50, default);
        await svc.RollAsync(1, "u", 100, 4, default);
        var second = await svc.RollAsync(1, "u", 100, 4, default);
        Assert.Equal(CubeRollOutcome.NoBet, second.Outcome);
    }

    [Fact]
    public async Task RollAsync_UnknownFace_NoPayout()
    {
        var bets = new InMemoryDiceCubeBetStore();
        var svc = MakeService(bets: bets);
        await svc.PlaceBetAsync(1, "u", 100, 50, default);
        var result = await svc.RollAsync(1, "u", 100, 99, default);
        Assert.Equal(0, result.Payout);
        Assert.Equal(0, result.Multiplier);
    }

    [Fact]
    public async Task RollAsync_Success_ReturnsRolledOutcome()
    {
        var bets = new InMemoryDiceCubeBetStore();
        var svc = MakeService(bets: bets);
        await svc.PlaceBetAsync(1, "u", 100, 50, default);
        var result = await svc.RollAsync(1, "u", 100, 4, default);
        Assert.Equal(CubeRollOutcome.Rolled, result.Outcome);
    }

    [Fact]
    public async Task RollAsync_Success_ReturnsFace()
    {
        var bets = new InMemoryDiceCubeBetStore();
        var svc = MakeService(bets: bets);
        await svc.PlaceBetAsync(1, "u", 100, 50, default);
        var result = await svc.RollAsync(1, "u", 100, 5, default);
        Assert.Equal(5, result.Face);
    }

    [Fact]
    public async Task RollAsync_Success_ReturnsBetAmount()
    {
        var bets = new InMemoryDiceCubeBetStore();
        var svc = MakeService(bets: bets);
        await svc.PlaceBetAsync(1, "u", 100, 50, default);
        var result = await svc.RollAsync(1, "u", 100, 4, default);
        Assert.Equal(50, result.Bet);
    }

    [Fact]
    public async Task RollAsync_PublishesRollCompletedEvent()
    {
        var bus = new NullEventBus();
        var bets = new InMemoryDiceCubeBetStore();
        var svc = new DiceCubeService(new FakeEconomicsService(), new NullAnalyticsService(), bets, bus, NewCache(), Options.Create(new DiceCubeOptions()));
        await svc.PlaceBetAsync(1, "u", 100, 50, default);
        await svc.RollAsync(1, "u", 100, 4, default);
        Assert.Single(bus.Published);
        Assert.IsType<DiceCubeRollCompleted>(bus.Published[0]);
    }

    [Fact]
    public void BuildMultipliers_ContainsAllSixFaces()
    {
        var m = DiceCubeService.BuildMultipliers(new DiceCubeOptions());
        for (var face = 1; face <= 6; face++)
            Assert.True(m.ContainsKey(face));
    }

    [Fact]
    public void BuildMultipliers_Default_Face6_Is2()
    {
        var m = DiceCubeService.BuildMultipliers(new DiceCubeOptions());
        Assert.Equal(2, m[6]);
    }

    [Fact]
    public async Task PlaceBetAsync_CooldownAfterRoll_Blocks()
    {
        var cache = NewCache();
        var o = new DiceCubeOptions { MinSecondsBetweenBets = 30 };
        var bets = new InMemoryDiceCubeBetStore();
        var svc = MakeService(bets: bets, cache: cache, o: o);
        await svc.PlaceBetAsync(1, "u", 100, 10, default);
        await svc.RollAsync(1, "u", 100, 1, default);
        var r = await svc.PlaceBetAsync(1, "u", 100, 10, default);
        Assert.Equal(CubeBetError.Cooldown, r.Error);
        Assert.InRange(r.CooldownSeconds, 1, 30);
    }
}

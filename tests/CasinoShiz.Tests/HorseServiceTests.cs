using Games.Horse;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CasinoShiz.Tests;

public class HorseServiceTests
{
    private const long Scope = 1000;

    private static HorseService MakeService(
        FakeEconomicsService? economics = null,
        InMemoryHorseBetStore? bets = null,
        InMemoryHorseResultStore? results = null,
        int horseCount = 4,
        int minBetsToRun = 4,
        long[]? admins = null) =>
        new(
            bets ?? new InMemoryHorseBetStore(),
            results ?? new InMemoryHorseResultStore(),
            economics ?? new FakeEconomicsService(),
            new NullAnalyticsService(),
            new NullEventBus(),
            Options.Create(new HorseOptions
            {
                HorseCount = horseCount,
                MinBetsToRun = minBetsToRun,
                Admins = [.. (admins ?? [])],
            }),
            NullLogger<HorseService>.Instance);

    // ── GetKoefs (static formula) ─────────────────────────────────────────────

    [Fact]
    public void GetKoefs_AllZeroStakes_Returns1ForAllHorses()
    {
        var stakes = new Dictionary<int, int> { [0] = 0, [1] = 0, [2] = 0, [3] = 0 };
        var koefs = HorseService.GetKoefs(stakes);
        foreach (var k in koefs.Values) Assert.Equal(1.0, k);
    }

    [Fact]
    public void GetKoefs_SingleHorseAllStake_Returns1()
    {
        // Only horse 0 has bets; koef = (sum - stake) / (1.1 * stake) + 1 = 0 + 1 = 1
        var stakes = new Dictionary<int, int> { [0] = 100 };
        var koefs = HorseService.GetKoefs(stakes);
        Assert.Equal(1.0, koefs[0]);
    }

    [Fact]
    public void GetKoefs_EvenSplit_CorrectFormula()
    {
        // 2 horses, 100 each. stake=100, sum=200
        // koef = floor((200-100) / (1.1*100) * 1000) / 1000 + 1
        //      = floor(100/110 * 1000) / 1000 + 1
        //      = floor(909.09) / 1000 + 1
        //      = 909/1000 + 1 = 0.909 + 1 = 1.909
        var stakes = new Dictionary<int, int> { [0] = 100, [1] = 100 };
        var koefs = HorseService.GetKoefs(stakes);
        Assert.Equal(1.909, koefs[0], precision: 3);
        Assert.Equal(1.909, koefs[1], precision: 3);
    }

    [Fact]
    public void GetKoefs_FavoriteHasLowerKoef()
    {
        // Horse 0 has more bets → lower payout ratio
        var stakes = new Dictionary<int, int> { [0] = 300, [1] = 100 };
        var koefs = HorseService.GetKoefs(stakes);
        Assert.True(koefs[0] < koefs[1], "Favorite (more bets) should have lower koef");
    }

    [Fact]
    public void GetKoefs_ZeroStakeHorse_Returns1()
    {
        var stakes = new Dictionary<int, int> { [0] = 0, [1] = 200 };
        var koefs = HorseService.GetKoefs(stakes);
        Assert.Equal(1.0, koefs[0]);
    }

    [Fact]
    public void GetKoefs_ResultsFlooredToThreeDecimals()
    {
        var stakes = new Dictionary<int, int> { [0] = 100, [1] = 300 };
        var koefs = HorseService.GetKoefs(stakes);
        // Both values should be a multiple of 0.001
        foreach (var k in koefs.Values)
        {
            var rounded = Math.Round(k, 3);
            Assert.Equal(rounded, k);
        }
    }

    // ── PlaceBetAsync ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(5)] // > HorseCount=4
    public async Task PlaceBetAsync_InvalidHorseId_ReturnsInvalidHorseId(int horseId)
    {
        var svc = MakeService(horseCount: 4);
        var result = await svc.PlaceBetAsync(1, "u", Scope, horseId, 50, default);
        Assert.Equal(HorseError.InvalidHorseId, result.Error);
    }

    [Fact]
    public async Task PlaceBetAsync_ValidHorseId_BoundaryMin_Succeeds()
    {
        var svc = MakeService(horseCount: 4);
        var result = await svc.PlaceBetAsync(1, "u", Scope, horseId: 1, 50, default);
        Assert.Equal(HorseError.None, result.Error);
    }

    [Fact]
    public async Task PlaceBetAsync_ValidHorseId_BoundaryMax_Succeeds()
    {
        var svc = MakeService(horseCount: 4);
        var result = await svc.PlaceBetAsync(1, "u", Scope, horseId: 4, 50, default);
        Assert.Equal(HorseError.None, result.Error);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task PlaceBetAsync_InvalidAmount_ReturnsInvalidAmount(int amount)
    {
        var svc = MakeService();
        var result = await svc.PlaceBetAsync(1, "u", Scope, horseId: 1, amount, default);
        Assert.Equal(HorseError.InvalidAmount, result.Error);
    }

    [Fact]
    public async Task PlaceBetAsync_AmountExceedsBalance_ReturnsInvalidAmount()
    {
        var econ = new FakeEconomicsService { StartingBalance = 10 };
        var svc = MakeService(economics: econ);
        var result = await svc.PlaceBetAsync(1, "u", Scope, horseId: 1, 100, default);
        Assert.Equal(HorseError.InvalidAmount, result.Error);
    }

    [Fact]
    public async Task PlaceBetAsync_Success_ReturnsNoneError()
    {
        var svc = MakeService();
        var result = await svc.PlaceBetAsync(1, "u", Scope, horseId: 2, 50, default);
        Assert.Equal(HorseError.None, result.Error);
    }

    [Fact]
    public async Task PlaceBetAsync_Success_DebitsAmount()
    {
        var econ = new FakeEconomicsService();
        var svc = MakeService(economics: econ);
        await svc.PlaceBetAsync(1, "u", Scope, horseId: 1, 50, default);
        Assert.Single(econ.Debits);
        Assert.Equal(50, econ.Debits[0].Amount);
    }

    [Fact]
    public async Task PlaceBetAsync_Success_ReturnsRemainingBalance()
    {
        var econ = new FakeEconomicsService { StartingBalance = 200 };
        var svc = MakeService(economics: econ);
        var result = await svc.PlaceBetAsync(1, "u", Scope, horseId: 1, 50, default);
        Assert.Equal(150, result.RemainingCoins);
    }

    [Fact]
    public async Task PlaceBetAsync_Success_ReturnsHorseId()
    {
        var svc = MakeService();
        var result = await svc.PlaceBetAsync(1, "u", Scope, horseId: 3, 50, default);
        Assert.Equal(3, result.HorseId);
    }

    [Fact]
    public async Task PlaceBetAsync_Success_PublishesEvent()
    {
        var bus = new NullEventBus();
        var svc = new HorseService(
            new InMemoryHorseBetStore(), new InMemoryHorseResultStore(),
            new FakeEconomicsService(), new NullAnalyticsService(), bus,
            Options.Create(new HorseOptions { HorseCount = 4, MinBetsToRun = 1 }),
            NullLogger<HorseService>.Instance);

        await svc.PlaceBetAsync(1, "u", Scope, horseId: 1, 50, default);
        Assert.Single(bus.Published);
        Assert.IsType<HorseBetPlaced>(bus.Published[0]);
    }

    // ── GetTodayInfoAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetTodayInfoAsync_NoBets_ReturnsBetsCount0()
    {
        var svc = MakeService();
        var info = await svc.GetTodayInfoAsync(null, default);
        Assert.Equal(0, info.BetsCount);
    }

    [Fact]
    public async Task GetTodayInfoAsync_NoBets_KoefsAllOne()
    {
        var svc = MakeService(horseCount: 4);
        var info = await svc.GetTodayInfoAsync(null, default);
        Assert.All(info.Koefs.Values, k => Assert.Equal(1.0, k));
    }

    [Fact]
    public async Task GetTodayInfoAsync_AfterBets_ReturnsTotalCount()
    {
        var bets = new InMemoryHorseBetStore();
        var svc = MakeService(bets: bets, horseCount: 4);
        await svc.PlaceBetAsync(1, "u", Scope, 1, 100, default);
        await svc.PlaceBetAsync(2, "u", Scope, 2, 50, default);

        var info = await svc.GetTodayInfoAsync(null, default);
        Assert.Equal(2, info.BetsCount);
    }

    [Fact]
    public async Task GetTodayInfoAsync_FavoriteHorse_HasLowerKoef()
    {
        var bets = new InMemoryHorseBetStore();
        var svc = MakeService(bets: bets, horseCount: 4);
        // Bet 300 on horse 1, 100 on horse 2 → horse 1 is favorite
        await svc.PlaceBetAsync(1, "u", Scope, 1, 300, default);
        await svc.PlaceBetAsync(2, "u", Scope, 2, 100, default);

        var info = await svc.GetTodayInfoAsync(null, default);
        // horseId 1 → stored as index 0; horseId 2 → stored as index 1
        Assert.True(info.Koefs[0] < info.Koefs[1]);
    }

    [Fact]
    public async Task GetTodayInfoAsync_Scoped_ExcludesOtherBalanceScopes()
    {
        var bets = new InMemoryHorseBetStore();
        var svc = MakeService(bets: bets, horseCount: 4);
        await svc.PlaceBetAsync(1, "u", Scope, 1, 50, default);
        await svc.PlaceBetAsync(2, "u", 2000L, 2, 50, default);

        Assert.Equal(1, (await svc.GetTodayInfoAsync(Scope, default)).BetsCount);
        Assert.Equal(1, (await svc.GetTodayInfoAsync(2000L, default)).BetsCount);
        Assert.Equal(2, (await svc.GetTodayInfoAsync(null, default)).BetsCount);
    }

    // ── RunRaceAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task RunRaceAsync_NotAdmin_ReturnsNotAdmin()
    {
        var svc = MakeService(admins: [99L]);
        var result = await svc.RunRaceAsync(callerUserId: 1, HorseRunKind.Global, 0, default);
        Assert.Equal(HorseError.NotAdmin, result.Error);
    }

    [Fact]
    public async Task RunRaceAsync_NotEnoughBets_ReturnsNotEnoughBets()
    {
        var bets = new InMemoryHorseBetStore();
        var svc = MakeService(bets: bets, minBetsToRun: 3, admins: [1L]);
        await svc.PlaceBetAsync(2, "u", Scope, 1, 50, default); // only 1 bet, need 3
        var result = await svc.RunRaceAsync(callerUserId: 1, HorseRunKind.Global, 0, default);
        Assert.Equal(HorseError.NotEnoughBets, result.Error);
    }

    [Fact]
    public async Task RunRaceAsync_Success_ReturnsNoneError()
    {
        // horseCount=1 → winner is always horse 0
        var bets = new InMemoryHorseBetStore();
        var econ = new FakeEconomicsService();
        var svc = MakeService(economics: econ, bets: bets, horseCount: 1, minBetsToRun: 1, admins: [99L]);
        await svc.PlaceBetAsync(1, "u", Scope, 1, 100, default);

        var result = await svc.RunRaceAsync(callerUserId: 99, HorseRunKind.Global, 0, default);
        Assert.Equal(HorseError.None, result.Error);
    }

    [Fact]
    public async Task RunRaceAsync_SingleHorse_WinnerIsAlwaysHorse0()
    {
        var bets = new InMemoryHorseBetStore();
        var svc = MakeService(bets: bets, horseCount: 1, minBetsToRun: 1, admins: [99L]);
        await svc.PlaceBetAsync(1, "u", Scope, 1, 100, default);

        var result = await svc.RunRaceAsync(callerUserId: 99, HorseRunKind.Global, 0, default);
        Assert.Equal(0, result.Winner);
    }

    [Fact]
    public async Task RunRaceAsync_SingleHorse_CreditsBettors()
    {
        // With 1 horse, koef=1.0 → bettor gets 100 back
        var bets = new InMemoryHorseBetStore();
        var econ = new FakeEconomicsService();
        var svc = MakeService(economics: econ, bets: bets, horseCount: 1, minBetsToRun: 1, admins: [99L]);
        await svc.PlaceBetAsync(1, "u", Scope, 1, 100, default);

        await svc.RunRaceAsync(callerUserId: 99, HorseRunKind.Global, 0, default);

        Assert.Single(econ.Credits);
        Assert.Equal(100, econ.Credits[0].Amount);
    }

    [Fact]
    public async Task RunRaceAsync_SingleHorse_TransactionMatchesBet()
    {
        var bets = new InMemoryHorseBetStore();
        var svc = MakeService(bets: bets, horseCount: 1, minBetsToRun: 1, admins: [99L]);
        await svc.PlaceBetAsync(1, "u", Scope, 1, 75, default);

        var result = await svc.RunRaceAsync(callerUserId: 99, HorseRunKind.Global, 0, default);

        Assert.Single(result.Transactions);
        Assert.Equal(75, result.Transactions[0].Amount);
    }

    [Fact]
    public async Task RunRaceAsync_SingleHorse_GifBytesNotEmpty()
    {
        var bets = new InMemoryHorseBetStore();
        var svc = MakeService(bets: bets, horseCount: 1, minBetsToRun: 1, admins: [99L]);
        await svc.PlaceBetAsync(1, "u", Scope, 1, 50, default);

        var result = await svc.RunRaceAsync(callerUserId: 99, HorseRunKind.Global, 0, default);

        Assert.NotEmpty(result.GifBytes);
    }

    [Fact]
    public async Task RunRaceAsync_MultipleBettors_AllWinnersGetCredited()
    {
        // horseCount=1 → all bettors on horse 1 win
        var bets = new InMemoryHorseBetStore();
        var econ = new FakeEconomicsService { StartingBalance = 1_000 };
        var svc = MakeService(economics: econ, bets: bets, horseCount: 1, minBetsToRun: 1, admins: [99L]);
        await svc.PlaceBetAsync(1, "u1", Scope, 1, 100, default);
        await svc.PlaceBetAsync(2, "u2", Scope, 1, 200, default);

        await svc.RunRaceAsync(callerUserId: 99, HorseRunKind.Global, 0, default);

        Assert.Equal(2, econ.Credits.Count);
    }

    [Fact]
    public async Task RunRaceAsync_DeletesBetsAfterRace()
    {
        var bets = new InMemoryHorseBetStore();
        var svc = MakeService(bets: bets, horseCount: 1, minBetsToRun: 1, admins: [99L]);
        await svc.PlaceBetAsync(1, "u", Scope, 1, 50, default);
        await svc.RunRaceAsync(callerUserId: 99, HorseRunKind.Global, 0, default);

        // Running again should fail with NotEnoughBets since bets were deleted
        var second = await svc.RunRaceAsync(callerUserId: 99, HorseRunKind.Global, 0, default);
        Assert.Equal(HorseError.NotEnoughBets, second.Error);
    }

    [Fact]
    public async Task RunRaceAsync_ParticipantsListMatchesBettors()
    {
        var bets = new InMemoryHorseBetStore();
        var econ = new FakeEconomicsService { StartingBalance = 1_000 };
        var svc = MakeService(economics: econ, bets: bets, horseCount: 1, minBetsToRun: 1, admins: [99L]);
        await svc.PlaceBetAsync(1, "u1", Scope, 1, 100, default);
        await svc.PlaceBetAsync(2, "u2", Scope, 1, 50, default);

        var result = await svc.RunRaceAsync(callerUserId: 99, HorseRunKind.Global, 0, default);

        Assert.Equal(2, result.Participants.Count);
    }

    [Fact]
    public async Task RunRaceAsync_ThisChat_LeavesOtherScopesIntact()
    {
        const long scopeA = 1000L, scopeB = 2000L;
        var bets = new InMemoryHorseBetStore();
        var econ = new FakeEconomicsService { StartingBalance = 500 };
        var svc = MakeService(economics: econ, bets: bets, horseCount: 1, minBetsToRun: 1, admins: [99L]);
        await svc.PlaceBetAsync(1, "u", scopeA, 1, 50, default);
        await svc.PlaceBetAsync(2, "u", scopeB, 1, 50, default);

        await svc.RunRaceAsync(99, HorseRunKind.ThisChat, scopeA, default);

        Assert.Equal(0, (await svc.GetTodayInfoAsync(scopeA, default)).BetsCount);
        Assert.Equal(1, (await svc.GetTodayInfoAsync(scopeB, default)).BetsCount);
    }
}

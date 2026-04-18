using CasinoShiz.Configuration;
using CasinoShiz.Data;
using CasinoShiz.Data.Entities;
using CasinoShiz.Services.Analytics;
using CasinoShiz.Services.Economics;
using CasinoShiz.Services.Horse;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CasinoShiz.Tests;

public class HorseServiceTests
{
    private static (HorseService svc, AppDbContext db) Build(BotOptions? opts = null)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new AppDbContext(options);
        var reporter = new ClickHouseReporter(
            Options.Create(new ClickHouseOptions { Enabled = false }),
            NullLogger<ClickHouseReporter>.Instance);
        var economics = new EconomicsService(db, NullLogger<EconomicsService>.Instance);
        var bot = opts ?? new BotOptions { Token = "test", Admins = [999] };
        var svc = new HorseService(
            db, Options.Create(bot), reporter, economics,
            NullLogger<HorseService>.Instance);
        return (svc, db);
    }

    [Fact]
    public async Task PlaceBetAsync_InvalidHorseId_RejectsWithoutTouchingBalance()
    {
        var (svc, db) = Build();
        db.Users.Add(new UserState { TelegramUserId = 1, DisplayName = "u", Coins = 100 });
        await db.SaveChangesAsync();

        var r = await svc.PlaceBetAsync(1, "u", horseId: 9, amount: 10, default);
        Assert.Equal(HorseError.InvalidHorseId, r.Error);

        var user = await db.Users.FindAsync(1L);
        Assert.Equal(100, user!.Coins);
    }

    [Fact]
    public async Task PlaceBetAsync_AmountExceedsBalance_Rejects()
    {
        var (svc, db) = Build();
        db.Users.Add(new UserState { TelegramUserId = 1, DisplayName = "u", Coins = 10 });
        await db.SaveChangesAsync();

        var r = await svc.PlaceBetAsync(1, "u", horseId: 1, amount: 50, default);
        Assert.Equal(HorseError.InvalidAmount, r.Error);
    }

    [Fact]
    public async Task PlaceBetAsync_NewUser_IsSeededAutomatically()
    {
        // Users in a group chat who haven't rolled dice yet may still place bets.
        var (svc, db) = Build();
        var r = await svc.PlaceBetAsync(42, "fresh", horseId: 1, amount: 20, default);
        Assert.Equal(HorseError.None, r.Error);

        var user = await db.Users.FindAsync(42L);
        Assert.NotNull(user);
        Assert.Equal(80, user!.Coins); // 100 seed - 20 bet
    }

    [Fact]
    public async Task GetTodayInfoAsync_ReflectsAllGroupChatBets()
    {
        var (svc, db) = Build();
        db.Users.Add(new UserState { TelegramUserId = 1, DisplayName = "a", Coins = 500 });
        db.Users.Add(new UserState { TelegramUserId = 2, DisplayName = "b", Coins = 500 });
        db.Users.Add(new UserState { TelegramUserId = 3, DisplayName = "c", Coins = 500 });
        await db.SaveChangesAsync();

        await svc.PlaceBetAsync(1, "a", horseId: 1, amount: 30, default);
        await svc.PlaceBetAsync(2, "b", horseId: 2, amount: 20, default);
        await svc.PlaceBetAsync(3, "c", horseId: 1, amount: 50, default);

        var info = await svc.GetTodayInfoAsync(default);
        Assert.Equal(3, info.BetsCount);
        Assert.Equal(HorseService.HorseCount, info.Koefs.Count);
    }

    [Fact]
    public async Task RunRaceAsync_NonAdmin_Rejected()
    {
        var (svc, _) = Build();
        var outcome = await svc.RunRaceAsync(callerUserId: 1, default);
        Assert.Equal(HorseError.NotAdmin, outcome.Error);
    }

    [Fact]
    public async Task RunRaceAsync_AdminWithGroupBets_ClearsAndPaysWinners()
    {
        var (svc, db) = Build();
        db.Users.Add(new UserState { TelegramUserId = 1, DisplayName = "a", Coins = 500 });
        db.Users.Add(new UserState { TelegramUserId = 2, DisplayName = "b", Coins = 500 });
        db.Users.Add(new UserState { TelegramUserId = 3, DisplayName = "c", Coins = 500 });
        await db.SaveChangesAsync();

        // One user per horse + spread on all 4 horses so koefs are finite for whichever wins.
        for (int horse = 1; horse <= HorseService.HorseCount; horse++)
            await svc.PlaceBetAsync(1, "a", horseId: horse, amount: 10, default);
        await svc.PlaceBetAsync(2, "b", horseId: 1, amount: 20, default);
        await svc.PlaceBetAsync(3, "c", horseId: 2, amount: 30, default);

        var outcome = await svc.RunRaceAsync(callerUserId: 999, default);
        Assert.Equal(HorseError.None, outcome.Error);
        Assert.NotEmpty(outcome.GifBytes);

        // Bets table must be cleared after the race ran.
        var raceDate = CasinoShiz.Helpers.TimeHelper.GetRaceDate();
        var remaining = await db.HorseBets.CountAsync(b => b.RaceDate == raceDate);
        Assert.Equal(0, remaining);

        // Result row must exist for the day.
        var stored = await db.HorseResults.FindAsync(raceDate);
        Assert.NotNull(stored);
        Assert.Equal(outcome.Winner, stored!.Winner);
    }

    [Fact]
    public void GetKoefs_NoBets_AllInfinite()
    {
        var stakes = new Dictionary<int, int> { { 0, 0 }, { 1, 0 }, { 2, 0 }, { 3, 0 } };
        var ks = HorseService.GetKoefs(stakes);
        Assert.All(ks.Values, v => Assert.False(double.IsFinite(v)));
    }

    [Fact]
    public void GetKoefs_OneSidedBet_LooserHorsesHaveFiniteKoef()
    {
        // user 1 bets 100 on horse 0, user 2 bets 50 on horse 1, others 0.
        var stakes = new Dictionary<int, int> { { 0, 100 }, { 1, 50 }, { 2, 0 }, { 3, 0 } };
        var ks = HorseService.GetKoefs(stakes);

        Assert.True(double.IsFinite(ks[0]));
        Assert.True(double.IsFinite(ks[1]));
        Assert.True(ks[0] >= 1.0);
        Assert.True(ks[1] >= 1.0);
        Assert.False(double.IsFinite(ks[2]));
        Assert.False(double.IsFinite(ks[3]));
    }
}

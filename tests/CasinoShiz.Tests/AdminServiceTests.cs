using CasinoShiz.Configuration;
using CasinoShiz.Data;
using CasinoShiz.Data.Entities;
using CasinoShiz.Services.Admin;
using CasinoShiz.Services.Analytics;
using CasinoShiz.Services.Economics;
using CasinoShiz.Services.Poker.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CasinoShiz.Tests;

public class AdminServiceTests
{
    private static (AdminService svc, AppDbContext db) Build()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new AppDbContext(options);
        var reporter = new ClickHouseReporter(
            Options.Create(new ClickHouseOptions { Enabled = false }),
            NullLogger<ClickHouseReporter>.Instance);
        var bot = Options.Create(new BotOptions { Token = "test" });
        var economics = new EconomicsService(db, NullLogger<EconomicsService>.Instance);
        var poker = new PokerService(db, bot, reporter, economics, NullLogger<PokerService>.Instance);
        return (new AdminService(db, reporter, poker, economics), db);
    }

    [Fact]
    public async Task CancelBlackjackHand_refundsBetAndDeletesRow()
    {
        var (svc, db) = Build();
        db.Users.Add(new UserState { TelegramUserId = 1, DisplayName = "u", Coins = 50 });
        db.BlackjackHands.Add(new BlackjackHand
        {
            UserId = 1, Bet = 75,
            PlayerCards = "TS KH", DealerCards = "9D 8C", DeckState = "",
        });
        await db.SaveChangesAsync();

        var r = await svc.CancelBlackjackHandAsync(callerId: 99, targetUserId: 1, default);
        Assert.Equal(AdminCancelOp.Done, r.Op);
        Assert.Equal(75, r.Refunded);

        var user = await db.Users.FindAsync(1L);
        Assert.Equal(125, user!.Coins);
        Assert.Null(await db.BlackjackHands.FindAsync(1L));
    }

    [Fact]
    public async Task CancelBlackjackHand_noActiveHand_noop()
    {
        var (svc, _) = Build();
        var r = await svc.CancelBlackjackHandAsync(callerId: 99, targetUserId: 42, default);
        Assert.Equal(AdminCancelOp.Noop, r.Op);
    }

    [Fact]
    public async Task GetOverviewStats_includesHorseDiceFreespins()
    {
        var (svc, db) = Build();
        var today = CasinoShiz.Helpers.TimeHelper.GetCurrentDayMillis();
        var raceDate = CasinoShiz.Helpers.TimeHelper.GetRaceDate();

        db.Users.Add(new UserState
        {
            TelegramUserId = 1, DisplayName = "u1", Coins = 100,
            LastDayUtc = today, AttemptCount = 2,
        });
        db.Users.Add(new UserState
        {
            TelegramUserId = 2, DisplayName = "u2", Coins = 100,
            LastDayUtc = today, AttemptCount = 1,
        });
        db.Users.Add(new UserState
        {
            TelegramUserId = 3, DisplayName = "stale", Coins = 0,
            LastDayUtc = 0, AttemptCount = 99,
        });
        db.HorseBets.Add(new HorseBet { Id = Guid.NewGuid(), RaceDate = raceDate, HorseId = 0, Amount = 25, UserId = 1 });
        db.HorseBets.Add(new HorseBet { Id = Guid.NewGuid(), RaceDate = raceDate, HorseId = 1, Amount = 15, UserId = 2 });
        db.HorseBets.Add(new HorseBet { Id = Guid.NewGuid(), RaceDate = "01-01-1970", HorseId = 0, Amount = 999, UserId = 1 });
        db.HorseResults.Add(new HorseResult { RaceDate = "01-01-1970", Winner = 0, ImageData = [] });
        db.FreespinCodes.Add(new FreespinCode { Code = Guid.NewGuid(), Active = true, IssuedBy = 1, IssuedAt = 0 });
        db.FreespinCodes.Add(new FreespinCode { Code = Guid.NewGuid(), Active = false, IssuedBy = 2, IssuedAt = 0 });
        await db.SaveChangesAsync();

        var stats = await svc.GetOverviewStatsAsync(default);

        Assert.Equal(3, stats.TotalUsers);
        Assert.Equal(2, stats.HorseBetsToday);
        Assert.Equal(40, stats.HorsePotToday);
        Assert.Equal(1, stats.HorseRacesRun);
        Assert.Equal(3, stats.DiceAttemptsToday); // stale user excluded
        Assert.Equal(1, stats.ActiveFreespinCodes);
    }
}

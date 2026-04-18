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
}

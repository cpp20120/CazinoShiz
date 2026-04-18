using CasinoShiz.Configuration;
using CasinoShiz.Data;
using CasinoShiz.Data.Entities;
using CasinoShiz.Services.Analytics;
using CasinoShiz.Services.Blackjack;
using CasinoShiz.Services.Economics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CasinoShiz.Tests;

public class BlackjackServiceTests
{
    private static (BlackjackService svc, AppDbContext db) Build()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new AppDbContext(options);
        var bot = new BotOptions { Token = "test", BlackjackMinBet = 10, BlackjackMaxBet = 500 };
        var reporter = new ClickHouseReporter(
            Options.Create(new ClickHouseOptions { Enabled = false }),
            NullLogger<ClickHouseReporter>.Instance);
        var economics = new EconomicsService(db, NullLogger<EconomicsService>.Instance);
        return (new BlackjackService(db, Options.Create(bot), reporter, economics), db);
    }

    private static async Task SeedHand(AppDbContext db, long userId, int coins, int bet,
        string player, string dealer, string deck)
    {
        db.Users.Add(new UserState { TelegramUserId = userId, DisplayName = "t", Coins = coins });
        db.BlackjackHands.Add(new BlackjackHand
        {
            UserId = userId, Bet = bet,
            PlayerCards = player, DealerCards = dealer, DeckState = deck,
            ChatId = 0, CreatedAt = 0,
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task StartAsync_betBelowMin_returnsInvalidBet()
    {
        var (svc, db) = Build();
        db.Users.Add(new UserState { TelegramUserId = 1, DisplayName = "u", Coins = 1000 });
        await db.SaveChangesAsync();

        var r = await svc.StartAsync(1, "u", 0, bet: 5, ct: default);
        Assert.Equal(BlackjackError.InvalidBet, r.Error);
    }

    [Fact]
    public async Task StartAsync_notEnoughCoins_returnsError()
    {
        var (svc, db) = Build();
        db.Users.Add(new UserState { TelegramUserId = 1, DisplayName = "u", Coins = 5 });
        await db.SaveChangesAsync();

        var r = await svc.StartAsync(1, "u", 0, bet: 50, ct: default);
        Assert.Equal(BlackjackError.NotEnoughCoins, r.Error);
    }

    [Fact]
    public async Task StandAsync_playerHigher_wins2xBet()
    {
        var (svc, db) = Build();
        await SeedHand(db, 1, coins: 50, bet: 50, "TS KH", "TH 8C", deck: "");

        var r = await svc.StandAsync(1, default);
        Assert.Equal(BlackjackError.None, r.Error);
        Assert.Equal(BlackjackOutcome.PlayerWin, r.Snapshot!.Outcome);

        var user = await db.Users.FindAsync(1L);
        Assert.Equal(150, user!.Coins);
        Assert.Null(await db.BlackjackHands.FindAsync(1L));
    }

    [Fact]
    public async Task HitAsync_bust_losesBet()
    {
        var (svc, db) = Build();
        await SeedHand(db, 1, coins: 50, bet: 50, "TS KH", "TH 8C", deck: "TC 2D");

        var r = await svc.HitAsync(1, default);
        Assert.Equal(BlackjackOutcome.PlayerBust, r.Snapshot!.Outcome);

        var user = await db.Users.FindAsync(1L);
        Assert.Equal(50, user!.Coins);
    }

    [Fact]
    public async Task StandAsync_naturalBlackjack_pays3to2()
    {
        var (svc, db) = Build();
        await SeedHand(db, 1, coins: 50, bet: 50, "AS KH", "TH 8C", deck: "");

        var r = await svc.StandAsync(1, default);
        Assert.Equal(BlackjackOutcome.PlayerBlackjack, r.Snapshot!.Outcome);

        var user = await db.Users.FindAsync(1L);
        Assert.Equal(175, user!.Coins);
    }

    [Fact]
    public async Task StandAsync_equalTotals_pushes()
    {
        var (svc, db) = Build();
        await SeedHand(db, 1, coins: 50, bet: 50, "TS 9H", "TH 9C", deck: "");

        var r = await svc.StandAsync(1, default);
        Assert.Equal(BlackjackOutcome.Push, r.Snapshot!.Outcome);

        var user = await db.Users.FindAsync(1L);
        Assert.Equal(100, user!.Coins);
    }

    [Fact]
    public async Task StandAsync_dealerHitsUntil17()
    {
        var (svc, db) = Build();
        await SeedHand(db, 1, coins: 50, bet: 50, "TS 9H", "5H 6C", deck: "7D 2C");

        var r = await svc.StandAsync(1, default);
        Assert.Equal(BlackjackOutcome.PlayerWin, r.Snapshot!.Outcome);
        Assert.Equal(18, r.Snapshot.DealerTotal);
    }

    [Fact]
    public async Task DoubleAsync_drawsOneAndSettles()
    {
        var (svc, db) = Build();
        await SeedHand(db, 1, coins: 100, bet: 50, "5S 6H", "TH 8C", deck: "TD 2C");

        var r = await svc.DoubleAsync(1, default);
        Assert.NotNull(r.Snapshot);
        Assert.Equal(21, r.Snapshot!.PlayerTotal);
        Assert.Equal(100, r.Snapshot.Bet);
        Assert.Equal(BlackjackOutcome.PlayerWin, r.Snapshot.Outcome);

        var user = await db.Users.FindAsync(1L);
        Assert.Equal(250, user!.Coins);
    }

    [Fact]
    public async Task DoubleAsync_afterThirdCard_rejected()
    {
        var (svc, db) = Build();
        await SeedHand(db, 1, coins: 100, bet: 50, "5S 6H 2D", "TH 8C", deck: "TD");

        var r = await svc.DoubleAsync(1, default);
        Assert.Equal(BlackjackError.CannotDouble, r.Error);
    }

    [Fact]
    public async Task HitAsync_noHand_returnsError()
    {
        var (svc, _) = Build();
        var r = await svc.HitAsync(999, default);
        Assert.Equal(BlackjackError.NoActiveHand, r.Error);
    }
}

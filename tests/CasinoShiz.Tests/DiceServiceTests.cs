using CasinoShiz.Configuration;
using CasinoShiz.Data;
using CasinoShiz.Data.Entities;
using CasinoShiz.Services.Analytics;
using CasinoShiz.Services.Dice;
using CasinoShiz.Services.Economics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CasinoShiz.Tests;

public class DiceServiceTests
{
    private static (DiceService svc, AppDbContext db) Build(BotOptions? opts = null)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new AppDbContext(options);
        var bot = opts ?? new BotOptions
        {
            Token = "test",
            AttemptsLimit = 3,
            DiceCost = 7,
            FreecodeProbability = 0,
        };
        var reporter = new ClickHouseReporter(
            Options.Create(new ClickHouseOptions { Enabled = false }),
            NullLogger<ClickHouseReporter>.Instance);
        var economics = new EconomicsService(db, NullLogger<EconomicsService>.Instance);
        var svc = new DiceService(db, Options.Create(bot), reporter, economics);
        return (svc, db);
    }

    [Fact]
    public async Task PlayAsync_Forwarded_ReturnsForwardedOutcome()
    {
        var (svc, _) = Build();
        var result = await svc.PlayAsync(1, "u", diceValue: 1, chatId: 100,
            isForwarded: true, isPrivateChat: false, ct: default);
        Assert.Equal(DiceOutcome.Forwarded, result.Outcome);
    }

    [Fact]
    public async Task PlayAsync_NewUser_SeedsWith100Coins()
    {
        var (svc, db) = Build();
        await svc.PlayAsync(42, "bob", diceValue: 1, chatId: 100,
            isForwarded: false, isPrivateChat: true, ct: default);
        var user = await db.Users.FindAsync(42L);
        Assert.NotNull(user);
        Assert.Equal("bob", user!.DisplayName);
    }

    [Fact]
    public async Task PlayAsync_NotEnoughCoins_ReturnsOutcome()
    {
        var (svc, db) = Build();
        db.Users.Add(new UserState
        {
            TelegramUserId = 1, DisplayName = "broke", Coins = 0,
            LastDayUtc = 0, AttemptCount = 0, ExtraAttempts = 0,
        });
        await db.SaveChangesAsync();

        var result = await svc.PlayAsync(1, "broke", diceValue: 1, chatId: 100,
            isForwarded: false, isPrivateChat: true, ct: default);
        Assert.Equal(DiceOutcome.NotEnoughCoins, result.Outcome);
        Assert.True(result.Loss > 0);
    }

    [Fact]
    public async Task PlayAsync_AttemptsExhausted_ReturnsAttemptsLimit()
    {
        var (svc, db) = Build();
        db.Users.Add(new UserState
        {
            TelegramUserId = 1, DisplayName = "limited", Coins = 10000,
            LastDayUtc = CasinoShiz.Helpers.TimeHelper.GetCurrentDayMillis(),
            AttemptCount = 3, ExtraAttempts = 0,
        });
        await db.SaveChangesAsync();

        var result = await svc.PlayAsync(1, "limited", diceValue: 1, chatId: 100,
            isForwarded: false, isPrivateChat: true, ct: default);
        Assert.Equal(DiceOutcome.AttemptsLimit, result.Outcome);
        Assert.Equal(3, result.TotalAttempts);
    }

    [Fact]
    public async Task PlayAsync_WithExtraAttempts_EntersRedeemMode()
    {
        var (svc, db) = Build();
        db.Users.Add(new UserState
        {
            TelegramUserId = 1, DisplayName = "redeemer", Coins = 10000,
            LastDayUtc = CasinoShiz.Helpers.TimeHelper.GetCurrentDayMillis(),
            AttemptCount = 3, ExtraAttempts = 2,
        });
        await db.SaveChangesAsync();

        var result = await svc.PlayAsync(1, "redeemer", diceValue: 1, chatId: 100,
            isForwarded: false, isPrivateChat: true, ct: default);
        Assert.Equal(DiceOutcome.Played, result.Outcome);
        Assert.Equal(30, result.Loss);
    }

    [Fact]
    public async Task PlayAsync_Success_DecrementsCoinsAndIncrementsAttempts()
    {
        var (svc, db) = Build();
        db.Users.Add(new UserState
        {
            TelegramUserId = 7, DisplayName = "p", Coins = 500,
            LastDayUtc = CasinoShiz.Helpers.TimeHelper.GetCurrentDayMillis(),
            AttemptCount = 0, ExtraAttempts = 0,
        });
        await db.SaveChangesAsync();

        var result = await svc.PlayAsync(7, "p", diceValue: 1, chatId: 100,
            isForwarded: false, isPrivateChat: true, ct: default);
        Assert.Equal(DiceOutcome.Played, result.Outcome);

        var user = await db.Users.FindAsync(7L);
        Assert.Equal(1, user!.AttemptCount);
    }
}

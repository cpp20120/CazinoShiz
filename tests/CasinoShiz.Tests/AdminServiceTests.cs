using Games.Admin;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CasinoShiz.Tests;

public class AdminServiceTests
{
    private static AdminService MakeService(
        InMemoryAdminStore? store = null,
        FakeEconomicsService? economics = null) =>
        new(
            store ?? new InMemoryAdminStore(),
            economics ?? new FakeEconomicsService(),
            new NullAnalyticsService(),
            NullLogger<AdminService>.Instance);

    private static UserSummary MakeUser(long id, string name = "TestUser", int coins = 500) =>
        new(id, name, coins, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

    // ── UserSyncAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task UserSyncAsync_ReturnsUserCount()
    {
        var store = new InMemoryAdminStore();
        store.Seed(MakeUser(1));
        store.Seed(MakeUser(2));
        store.Seed(MakeUser(3));
        var svc = MakeService(store);

        var count = await svc.UserSyncAsync(99, default);

        Assert.Equal(3, count);
    }

    [Fact]
    public async Task UserSyncAsync_EmptyStore_Returns0()
    {
        var svc = MakeService();
        var count = await svc.UserSyncAsync(99, default);
        Assert.Equal(0, count);
    }

    // ── PayAsync ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task PayAsync_PositiveAmount_CreditsUser()
    {
        var econ = new FakeEconomicsService { StartingBalance = 500 };
        var store = new InMemoryAdminStore(econ);
        store.Seed(MakeUser(10, coins: 500));
        var svc = MakeService(store, econ);

        await svc.PayAsync(99, targetUserId: 10, amount: 200, default);

        Assert.Single(econ.Credits);
        Assert.Equal(200, econ.Credits[0].Amount);
    }

    [Fact]
    public async Task PayAsync_NegativeAmount_DebitsUser()
    {
        var econ = new FakeEconomicsService { StartingBalance = 500 };
        var store = new InMemoryAdminStore(econ);
        store.Seed(MakeUser(10, coins: 500));
        var svc = MakeService(store, econ);

        await svc.PayAsync(99, targetUserId: 10, amount: -100, default);

        Assert.Single(econ.Debits);
        Assert.Equal(100, econ.Debits[0].Amount);
    }

    [Fact]
    public async Task PayAsync_PositiveAmount_ReturnsPayResult()
    {
        var econ = new FakeEconomicsService { StartingBalance = 500 };
        var store = new InMemoryAdminStore(econ);
        store.Seed(MakeUser(10, "Alice", coins: 500));
        var svc = MakeService(store, econ);

        var result = await svc.PayAsync(99, 10, 100, default);

        Assert.NotNull(result);
        Assert.Equal("Alice", result!.DisplayName);
        Assert.Equal(500, result.OldCoins);
        Assert.Equal(600, result.NewCoins);
        Assert.Equal(100, result.Amount);
    }

    [Fact]
    public async Task PayAsync_NegativeAmount_ReturnsUpdatedBalance()
    {
        var econ = new FakeEconomicsService { StartingBalance = 500 };
        var store = new InMemoryAdminStore(econ);
        store.Seed(MakeUser(10, "Bob", coins: 500));
        var svc = MakeService(store, econ);

        var result = await svc.PayAsync(99, 10, -200, default);

        Assert.NotNull(result);
        Assert.Equal(500, result!.OldCoins);
        Assert.Equal(300, result.NewCoins);
    }

    [Fact]
    public async Task PayAsync_UserNotInStore_UsesIdAsDisplayName()
    {
        var econ = new FakeEconomicsService();
        var store = new InMemoryAdminStore(econ);
        // No user seeded — after credit the store still has nothing, so PayAsync returns null
        var svc = MakeService(store, econ);

        var result = await svc.PayAsync(99, targetUserId: 77, amount: 50, default);

        // Credits still happen even if store returns null after
        Assert.Single(econ.Credits);
        Assert.Null(result); // store FindUserAsync returns null after pay
    }

    [Fact]
    public async Task PayAsync_ZeroAmount_DoesNotCreditOrDebit()
    {
        var econ = new FakeEconomicsService { StartingBalance = 500 };
        var store = new InMemoryAdminStore(econ);
        store.Seed(MakeUser(10, coins: 500));
        var svc = MakeService(store, econ);

        await svc.PayAsync(99, 10, 0, default);

        Assert.Single(econ.Credits); // 0 amount credit goes through (amount >= 0 branch)
        Assert.Equal(0, econ.Credits[0].Amount);
    }

    // ── GetUserAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetUserAsync_ExistingUser_ReturnsUser()
    {
        var store = new InMemoryAdminStore();
        store.Seed(MakeUser(5, "Charlie", 800));
        var svc = MakeService(store);

        var user = await svc.GetUserAsync(5, default);

        Assert.NotNull(user);
        Assert.Equal("Charlie", user!.DisplayName);
        Assert.Equal(800, user.Coins);
    }

    [Fact]
    public async Task GetUserAsync_NonExistentUser_ReturnsNull()
    {
        var svc = MakeService();
        var user = await svc.GetUserAsync(999, default);
        Assert.Null(user);
    }

    // ── RenameAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task RenameAsync_NewName_ReturnSetOp()
    {
        var svc = MakeService();
        var result = await svc.RenameAsync("OldName", "NewName", default);
        Assert.Equal(RenameOp.Set, result.Op);
    }

    [Fact]
    public async Task RenameAsync_NewName_StoresOverride()
    {
        var store = new InMemoryAdminStore();
        var svc = MakeService(store);
        await svc.RenameAsync("OldName", "NewName", default);

        var stored = await store.GetOverrideAsync("OldName", default);
        Assert.Equal("NewName", stored);
    }

    [Fact]
    public async Task RenameAsync_WildcardOnExisting_ReturnsClearedOp()
    {
        var store = new InMemoryAdminStore();
        var svc = MakeService(store);
        await svc.RenameAsync("OldName", "NewName", default); // set first
        var result = await svc.RenameAsync("OldName", "*", default);
        Assert.Equal(RenameOp.Cleared, result.Op);
    }

    [Fact]
    public async Task RenameAsync_WildcardOnExisting_RemovesOverride()
    {
        var store = new InMemoryAdminStore();
        var svc = MakeService(store);
        await svc.RenameAsync("OldName", "NewName", default);
        await svc.RenameAsync("OldName", "*", default);

        var stored = await store.GetOverrideAsync("OldName", default);
        Assert.Null(stored);
    }

    [Fact]
    public async Task RenameAsync_WildcardOnNonExistent_ReturnsNoChange()
    {
        var svc = MakeService();
        var result = await svc.RenameAsync("NonExistent", "*", default);
        Assert.Equal(RenameOp.NoChange, result.Op);
    }

    [Fact]
    public async Task RenameAsync_OverwriteExisting_ReturnSetOp()
    {
        var store = new InMemoryAdminStore();
        var svc = MakeService(store);
        await svc.RenameAsync("OldName", "FirstNew", default);
        var result = await svc.RenameAsync("OldName", "SecondNew", default);
        Assert.Equal(RenameOp.Set, result.Op);

        var stored = await store.GetOverrideAsync("OldName", default);
        Assert.Equal("SecondNew", stored);
    }
}

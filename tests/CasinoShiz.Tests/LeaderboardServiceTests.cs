using Games.Leaderboard;
using Microsoft.Extensions.Options;
using Xunit;

namespace CasinoShiz.Tests;

public class LeaderboardServiceTests
{
    private static readonly long NowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    private static readonly long RecentMs = NowMs - 1_000;           // 1 second ago — active
    private static readonly long StaleMs = NowMs - 10 * 24 * 3600 * 1_000L; // 10 days ago — inactive (> 7 day default)

    private static LeaderboardService MakeService(
        InMemoryLeaderboardStore? store = null,
        FakeEconomicsService? economics = null,
        int daysToHide = 7) =>
        new(
            store ?? new InMemoryLeaderboardStore(),
            economics ?? new FakeEconomicsService(),
            Options.Create(new LeaderboardOptions { DaysOfInactivityToHide = daysToHide }));

    // ── GetTopAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetTopAsync_Empty_ReturnsEmptyLeaderboard()
    {
        var svc = MakeService();
        var lb = await svc.GetTopAsync(10, default);
        Assert.Empty(lb.Places);
        Assert.False(lb.Truncated);
    }

    [Fact]
    public async Task GetTopAsync_ActiveUsers_ReturnsSortedByCoins()
    {
        var store = new InMemoryLeaderboardStore();
        store.Seed(1, "Alice", 500, RecentMs);
        store.Seed(2, "Bob", 300, RecentMs);
        store.Seed(3, "Carol", 900, RecentMs);
        var svc = MakeService(store);

        var lb = await svc.GetTopAsync(10, default);

        var coins = lb.Places.SelectMany(p => p.Users).Select(u => u.Coins).ToList();
        Assert.Equal([900, 500, 300], coins);
    }

    [Fact]
    public async Task GetTopAsync_InactiveUsers_AreFiltered()
    {
        var store = new InMemoryLeaderboardStore();
        store.Seed(1, "Active", 500, RecentMs);
        store.Seed(2, "Inactive", 9999, StaleMs); // high coins but stale
        var svc = MakeService(store);

        var lb = await svc.GetTopAsync(10, default);

        var allUsers = lb.Places.SelectMany(p => p.Users).ToList();
        Assert.DoesNotContain(allUsers, u => u.DisplayName == "Inactive");
        Assert.Single(allUsers);
    }

    [Fact]
    public async Task GetTopAsync_UsersWithSameCoins_GroupedInSamePlace()
    {
        var store = new InMemoryLeaderboardStore();
        store.Seed(1, "Alice", 500, RecentMs);
        store.Seed(2, "Bob", 500, RecentMs);
        store.Seed(3, "Carol", 300, RecentMs);
        var svc = MakeService(store);

        var lb = await svc.GetTopAsync(10, default);

        Assert.Equal(2, lb.Places.Count);
        Assert.Equal(2, lb.Places[0].Users.Count); // Alice and Bob tied at rank 1
        Assert.Single(lb.Places[1].Users);          // Carol at rank 2
    }

    [Fact]
    public async Task GetTopAsync_PlaceNumbers_AreCorrect()
    {
        var store = new InMemoryLeaderboardStore();
        store.Seed(1, "Alice", 500, RecentMs);
        store.Seed(2, "Bob", 300, RecentMs);
        var svc = MakeService(store);

        var lb = await svc.GetTopAsync(10, default);

        Assert.Equal(1, lb.Places[0].Place);
        Assert.Equal(2, lb.Places[1].Place);
    }

    [Fact]
    public async Task GetTopAsync_LimitReached_SetsTruncated()
    {
        var store = new InMemoryLeaderboardStore();
        store.Seed(1, "A", 900, RecentMs);
        store.Seed(2, "B", 800, RecentMs);
        store.Seed(3, "C", 700, RecentMs);
        var svc = MakeService(store);

        // limit=2, but there are 3 users → truncated
        var lb = await svc.GetTopAsync(2, default);

        Assert.True(lb.Truncated);
    }

    [Fact]
    public async Task GetTopAsync_LimitNotReached_NotTruncated()
    {
        var store = new InMemoryLeaderboardStore();
        store.Seed(1, "A", 500, RecentMs);
        store.Seed(2, "B", 300, RecentMs);
        var svc = MakeService(store);

        var lb = await svc.GetTopAsync(10, default);

        Assert.False(lb.Truncated);
    }

    [Fact]
    public async Task GetTopAsync_ZeroLimit_ReturnsAll()
    {
        var store = new InMemoryLeaderboardStore();
        for (var i = 0; i < 20; i++)
            store.Seed(i, $"U{i}", 100 - i, RecentMs);
        var svc = MakeService(store);

        var lb = await svc.GetTopAsync(limit: 0, default);

        Assert.Equal(20, lb.Places.Count);
        Assert.False(lb.Truncated);
    }

    [Fact]
    public async Task GetTopAsync_DaysToHide_ControlsCutoff()
    {
        var store = new InMemoryLeaderboardStore();
        // User active 2 days ago — hidden with daysToHide=1, visible with daysToHide=3
        var twoDaysAgoMs = NowMs - 2 * 24 * 3600 * 1_000L;
        store.Seed(1, "U1", 500, twoDaysAgoMs);
        var svc1 = MakeService(store, daysToHide: 1);
        var svc3 = MakeService(store, daysToHide: 3);

        var lb1 = await svc1.GetTopAsync(10, default);
        var lb3 = await svc3.GetTopAsync(10, default);

        Assert.Empty(lb1.Places);
        Assert.Single(lb3.Places);
    }

    // ── GetBalanceAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetBalanceAsync_UserInStore_ReturnsCoins()
    {
        var store = new InMemoryLeaderboardStore();
        store.Seed(1, "Alice", 750, RecentMs);
        var svc = MakeService(store);

        var info = await svc.GetBalanceAsync(1, "Alice", default);

        Assert.Equal(750, info.Coins);
    }

    [Fact]
    public async Task GetBalanceAsync_ActiveUser_IsVisible()
    {
        var store = new InMemoryLeaderboardStore();
        store.Seed(1, "Alice", 750, RecentMs);
        var svc = MakeService(store);

        var info = await svc.GetBalanceAsync(1, "Alice", default);

        Assert.True(info.Visible);
    }

    [Fact]
    public async Task GetBalanceAsync_InactiveUser_IsNotVisible()
    {
        var store = new InMemoryLeaderboardStore();
        store.Seed(1, "Alice", 750, StaleMs);
        var svc = MakeService(store);

        var info = await svc.GetBalanceAsync(1, "Alice", default);

        Assert.False(info.Visible);
    }

    [Fact]
    public async Task GetBalanceAsync_UserNotInStore_ReturnsZeroInvisible()
    {
        var svc = MakeService();

        var info = await svc.GetBalanceAsync(42, "Unknown", default);

        Assert.Equal(0, info.Coins);
        Assert.False(info.Visible);
    }
}

using BotFramework.Host;
using Microsoft.Extensions.Options;

namespace Games.Leaderboard;

public interface ILeaderboardService
{
    Task<Leaderboard> GetTopAsync(int limit, long balanceScopeId, CancellationToken ct);
    Task<BalanceInfo> GetBalanceAsync(long userId, long balanceScopeId, string displayName, CancellationToken ct);
    Task<GlobalLeaderboard> GetGlobalTopAsync(int limit, CancellationToken ct);
    Task<MultiChatLeaderboard> GetTopByChatAsync(int perChatLimit, CancellationToken ct);
}

public sealed class LeaderboardService(
    ILeaderboardStore store,
    IEconomicsService economics,
    IOptions<LeaderboardOptions> options) : ILeaderboardService
{
    private readonly LeaderboardOptions _opts = options.Value;

    public async Task<Leaderboard> GetTopAsync(int limit, long balanceScopeId, CancellationToken ct)
    {
        var since = ActiveSinceUnixMs();

        var active = await store.ListActiveAsync(since, balanceScopeId, ct);
        if (active.Count == 0) return new Leaderboard([], Truncated: false);

        var places = new List<LeaderboardPlace>();
        var lastBalance = active[0].Coins + 1;

        for (var i = 0; i < active.Count && (places.Count < limit || limit == 0); i++)
        {
            var user = active[i];
            if (user.Coins < lastBalance)
            {
                lastBalance = user.Coins;
                places.Add(new LeaderboardPlace(places.Count + 1, []));
            }
            places[^1].Users.Add(user);
        }

        var shown = places.Sum(p => p.Users.Count);
        var truncated = limit > 0 && places.Count >= limit && active.Count > shown;
        return new Leaderboard(places, truncated);
    }

    public async Task<BalanceInfo> GetBalanceAsync(
        long userId, long balanceScopeId, string displayName, CancellationToken ct)
    {
        await economics.EnsureUserAsync(userId, balanceScopeId, displayName, ct);
        var row = await store.FindAsync(userId, balanceScopeId, ct);
        if (row is not { } r) return new BalanceInfo(0, Visible: false);

        var visible = r.UpdatedAtUnixMs >= ActiveSinceUnixMs();
        return new BalanceInfo(r.Coins, visible);
    }

    public async Task<GlobalLeaderboard> GetGlobalTopAsync(int limit, CancellationToken ct)
    {
        var since = ActiveSinceUnixMs();
        var (users, totalUsers) = await store.ListGlobalAggregateAsync(since, limit, ct);

        if (users.Count == 0)
            return new GlobalLeaderboard([], Truncated: false, TotalUsers: 0);

        var places = new List<GlobalLeaderboardPlace>();
        var lastBalance = users[0].TotalCoins + 1;

        foreach (var user in users)
        {
            if (user.TotalCoins < lastBalance)
            {
                lastBalance = user.TotalCoins;
                places.Add(new GlobalLeaderboardPlace(places.Count + 1, []));
            }
            places[^1].Users.Add(user);
        }

        var truncated = limit > 0 && totalUsers > users.Count;
        return new GlobalLeaderboard(places, truncated, totalUsers);
    }

    public async Task<MultiChatLeaderboard> GetTopByChatAsync(int perChatLimit, CancellationToken ct)
    {
        var since = ActiveSinceUnixMs();
        var rows = await store.ListGlobalSplitAsync(since, perChatLimit, ct);
        if (rows.Count == 0) return new MultiChatLeaderboard([]);

        var grouped = rows
            .GroupBy(r => r.ChatId)
            .Select(g =>
            {
                var first = g.First();
                var places = BuildPlaces(g.Select(x => x.User).ToList());
                return (
                    Chat: new ChatLeaderboard(
                        ChatId: first.ChatId,
                        Title: first.Title,
                        ChatType: first.ChatType,
                        Places: places,
                        Truncated: false),
                    TopCoins: g.Max(x => x.User.Coins));
            })
            .OrderByDescending(x => x.TopCoins)
            .ThenBy(x => x.Chat.ChatId)
            .Select(x => x.Chat)
            .ToList();

        return new MultiChatLeaderboard(grouped);
    }

    private long ActiveSinceUnixMs()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return now - (long)_opts.DaysOfInactivityToHide * 24 * 60 * 60 * 1000;
    }

    private static List<LeaderboardPlace> BuildPlaces(IReadOnlyList<LeaderboardUser> sortedDesc)
    {
        var places = new List<LeaderboardPlace>();
        if (sortedDesc.Count == 0) return places;

        var lastBalance = sortedDesc[0].Coins + 1;
        foreach (var user in sortedDesc)
        {
            if (user.Coins < lastBalance)
            {
                lastBalance = user.Coins;
                places.Add(new LeaderboardPlace(places.Count + 1, []));
            }
            places[^1].Users.Add(user);
        }
        return places;
    }
}

using BotFramework.Host;
using Microsoft.Extensions.Options;

namespace Games.Leaderboard;

public interface ILeaderboardService
{
    Task<Leaderboard> GetTopAsync(int limit, CancellationToken ct);
    Task<BalanceInfo> GetBalanceAsync(long userId, string displayName, CancellationToken ct);
}

public sealed class LeaderboardService(
    ILeaderboardStore store,
    IEconomicsService economics,
    IOptions<LeaderboardOptions> options) : ILeaderboardService
{
    private readonly LeaderboardOptions _opts = options.Value;

    public async Task<Leaderboard> GetTopAsync(int limit, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var since = now - (long)_opts.DaysOfInactivityToHide * 24 * 60 * 60 * 1000;

        var active = await store.ListActiveAsync(since, ct);
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

    public async Task<BalanceInfo> GetBalanceAsync(long userId, string displayName, CancellationToken ct)
    {
        await economics.EnsureUserAsync(userId, displayName, ct);
        var row = await store.FindAsync(userId, ct);
        if (row is not { } r) return new BalanceInfo(0, Visible: false);

        var thresholdMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            - (long)_opts.DaysOfInactivityToHide * 24 * 60 * 60 * 1000;
        var visible = r.UpdatedAtUnixMs >= thresholdMs;
        return new BalanceInfo(r.Coins, visible);
    }
}

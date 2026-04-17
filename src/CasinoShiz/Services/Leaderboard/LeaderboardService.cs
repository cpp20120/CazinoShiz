using CasinoShiz.Configuration;
using CasinoShiz.Data;
using CasinoShiz.Data.Entities;
using CasinoShiz.Helpers;
using CasinoShiz.Services.Analytics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CasinoShiz.Services.Leaderboard;

public sealed class LeaderboardService(
    AppDbContext db,
    IOptions<BotOptions> options,
    ClickHouseReporter reporter)
{
    private readonly BotOptions _opts = options.Value;

    public async Task<Leaderboard> GetTopAsync(long chatId, int limit, CancellationToken ct)
    {
        var currentDay = TimeHelper.GetCurrentDay();
        var allUsers = await db.Users.ToListAsync(ct);
        var renames = await db.DisplayNameOverrides.ToDictionaryAsync(r => r.OriginalName, r => r.NewName, ct);

        var active = allUsers
            .Where(u => TimeHelper.GetDaysBetween(currentDay, TimeHelper.GetDateFromMillis(u.LastDayUtc)) < _opts.DaysOfInactivityToHideInTop)
            .Select(u => new LeaderboardUser(
                u.TelegramUserId,
                renames.GetValueOrDefault(u.DisplayName, u.DisplayName),
                u.Coins, u.LastDayUtc, u.AttemptCount, u.ExtraAttempts))
            .OrderByDescending(u => u.Coins)
            .ToList();

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

        if (places.Count > 0)
        {
            reporter.SendEvents(places[0].Users.Select(u => new EventData
            {
                EventType = "achievement",
                Payload = new { type = "first_place", balance = u.Coins, chat_id = chatId, user_id = u.TelegramUserId, createdAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }
            }));
        }

        var truncated = limit > 0 && places.Count >= limit && active.Count > places.Sum(p => p.Users.Count);
        return new Leaderboard(places, truncated);
    }

    public async Task<BalanceInfo> GetBalanceAsync(long userId, string displayName, CancellationToken ct)
    {
        var user = await db.Users.FindAsync([userId], ct);
        if (user == null)
        {
            user = new UserState
            {
                TelegramUserId = userId,
                DisplayName = displayName,
                Coins = 100,
                LastDayUtc = TimeHelper.GetCurrentDayMillis(),
            };
            db.Users.Add(user);
            await db.SaveChangesAsync(ct);
        }

        var visible = TimeHelper.GetDaysBetween(TimeHelper.GetCurrentDay(), TimeHelper.GetDateFromMillis(user.LastDayUtc))
            < _opts.DaysOfInactivityToHideInTop;

        return new BalanceInfo(user.Coins, visible);
    }
}

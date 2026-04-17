using CasinoShiz.Data;
using CasinoShiz.Data.Entities;
using CasinoShiz.Helpers;
using CasinoShiz.Services.Analytics;
using Microsoft.EntityFrameworkCore;

namespace CasinoShiz.Services.Admin;

public sealed class AdminService(
    AppDbContext db,
    ClickHouseReporter reporter)
{
    public async Task<int> UserSyncAsync(long callerId, CancellationToken ct)
    {
        var users = await db.Users.ToListAsync(ct);

        reporter.SendEvents(users.Select(u => new EventData
        {
            EventType = "user_map",
            Payload = new { display_name = u.DisplayName, user_id = u.TelegramUserId }
        }));

        reporter.SendEvent(new EventData
        {
            EventType = "admin_command",
            Payload = new { command = "usersync", calleeId = callerId, count = users.Count }
        });

        return users.Count;
    }

    public async Task<PayResult> PayAsync(long callerId, long targetUserId, int amount, CancellationToken ct)
    {
        var user = await db.Users.FindAsync([targetUserId], ct);
        if (user == null)
        {
            user = new UserState
            {
                TelegramUserId = targetUserId,
                DisplayName = $"User ID: {targetUserId}",
                Coins = 100,
                LastDayUtc = TimeHelper.GetCurrentDayMillis(),
            };
            db.Users.Add(user);
        }

        var oldCoins = user.Coins;
        user.Coins += amount;
        await db.SaveChangesAsync(ct);

        reporter.SendEvent(new EventData
        {
            EventType = "admin_command",
            Payload = new { command = "pay", calleeId = callerId, amount, forUserId = targetUserId }
        });

        return new PayResult(user.DisplayName, oldCoins, user.Coins, amount);
    }

    public async Task<UserLookup> GetUserAsync(long targetUserId, CancellationToken ct)
    {
        var user = await db.Users.FindAsync([targetUserId], ct);
        return new UserLookup(user);
    }

    public void ReportNotAdmin(long userId)
    {
        reporter.SendEvent(new EventData
        {
            EventType = "admin_command",
            Payload = new { type = "insufficient_permissions", command = "not_admin", calleeId = userId }
        });
    }

    public void ReportUserInfo(long callerId, string targetId)
    {
        reporter.SendEvent(new EventData
        {
            EventType = "admin_command",
            Payload = new { command = "userinfo", calleeId = callerId, requestedUserId = targetId }
        });
    }

    public async Task<RenameResult> RenameAsync(string oldName, string newName, CancellationToken ct)
    {
        var existing = await db.DisplayNameOverrides.FindAsync([oldName], ct);

        if (newName == "*")
        {
            if (existing == null) return new RenameResult(RenameOp.NoChange, oldName, newName);
            db.DisplayNameOverrides.Remove(existing);
            await db.SaveChangesAsync(ct);
            return new RenameResult(RenameOp.Cleared, oldName, newName);
        }

        if (existing != null)
            existing.NewName = newName;
        else
            db.DisplayNameOverrides.Add(new DisplayNameOverride { OriginalName = oldName, NewName = newName });

        await db.SaveChangesAsync(ct);
        return new RenameResult(RenameOp.Set, oldName, newName);
    }
}

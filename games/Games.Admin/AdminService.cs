using BotFramework.Host;

namespace Games.Admin;

public interface IAdminService
{
    Task<int> UserSyncAsync(long callerId, CancellationToken ct);
    /// <param name="balanceScopeId">Chat where the /run pay is executed — coins apply to that wallet.</param>
    Task<PayResult?> PayAsync(long callerId, long targetUserId, long balanceScopeId, int amount, CancellationToken ct);
    Task<UserSummary?> GetUserAsync(long targetUserId, long balanceScopeId, CancellationToken ct);
    Task<RenameResult> RenameAsync(string oldName, string newName, CancellationToken ct);
    void ReportNotAdmin(long userId);
    void ReportUserInfo(long callerId, string targetId);
}

public sealed partial class AdminService(
    IAdminStore store,
    IEconomicsService economics,
    IAnalyticsService analytics,
    ILogger<AdminService> logger) : IAdminService
{
    public async Task<int> UserSyncAsync(long callerId, CancellationToken ct)
    {
        var users = await store.ListUsersAsync(ct);

        foreach (var u in users)
        {
            analytics.Track("admin", "user_map", new Dictionary<string, object?>
            {
                ["user_id"] = u.TelegramUserId,
                ["display_name"] = u.DisplayName,
            });
        }

        analytics.Track("admin", "command", new Dictionary<string, object?>
        {
            ["command"] = "usersync",
            ["caller_id"] = callerId,
            ["count"] = users.Count,
        });

        LogUsersync(callerId, users.Count);
        return users.Count;
    }

    public async Task<PayResult?> PayAsync(
        long callerId, long targetUserId, long balanceScopeId, int amount, CancellationToken ct)
    {
        var before = await store.FindUserAsync(targetUserId, balanceScopeId, ct);
        var displayName = before?.DisplayName ?? $"User ID: {targetUserId}";
        await economics.EnsureUserAsync(targetUserId, balanceScopeId, displayName, ct);

        if (amount >= 0)
        {
            await economics.CreditAsync(targetUserId, balanceScopeId, amount, "admin.pay", ct);
        }
        else
        {
            await economics.DebitAsync(targetUserId, balanceScopeId, -amount, "admin.pay", ct);
        }

        var after = await store.FindUserAsync(targetUserId, balanceScopeId, ct);
        if (after == null) return null;

        analytics.Track("admin", "command", new Dictionary<string, object?>
        {
            ["command"] = "pay",
            ["caller_id"] = callerId,
            ["target_user_id"] = targetUserId,
            ["amount"] = amount,
        });

        var oldCoins = before?.Coins ?? 0;
        return new PayResult(after.DisplayName, oldCoins, after.Coins, amount);
    }

    public Task<UserSummary?> GetUserAsync(long targetUserId, long balanceScopeId, CancellationToken ct) =>
        store.FindUserAsync(targetUserId, balanceScopeId, ct);

    public async Task<RenameResult> RenameAsync(string oldName, string newName, CancellationToken ct)
    {
        var existing = await store.GetOverrideAsync(oldName, ct);

        if (newName == "*")
        {
            if (existing == null) return new RenameResult(RenameOp.NoChange, oldName, newName);
            await store.DeleteOverrideAsync(oldName, ct);
            return new RenameResult(RenameOp.Cleared, oldName, newName);
        }

        await store.UpsertOverrideAsync(oldName, newName, ct);
        return new RenameResult(RenameOp.Set, oldName, newName);
    }

    public void ReportNotAdmin(long userId)
    {
        analytics.Track("admin", "command", new Dictionary<string, object?>
        {
            ["command"] = "not_admin",
            ["caller_id"] = userId,
            ["type"] = "insufficient_permissions",
        });
    }

    public void ReportUserInfo(long callerId, string targetId)
    {
        analytics.Track("admin", "command", new Dictionary<string, object?>
        {
            ["command"] = "userinfo",
            ["caller_id"] = callerId,
            ["target_id"] = targetId,
        });
    }

    [LoggerMessage(LogLevel.Information, "admin.usersync caller={CallerId} count={Count}")]
    partial void LogUsersync(long callerId, int count);
}

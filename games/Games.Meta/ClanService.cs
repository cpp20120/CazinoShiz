namespace Games.Meta;

public interface IClanService
{
    Task<ClanCreateResult> CreateAsync(long chatId, long userId, string displayName, string tag, string name, CancellationToken ct);
    Task<ClanJoinResult> JoinAsync(long chatId, long userId, string displayName, string tag, CancellationToken ct);
    Task<ClanInfo?> GetUserClanAsync(long chatId, long userId, CancellationToken ct);
    Task<ClanInfo?> GetClanByTagAsync(long chatId, string tag, CancellationToken ct);
    Task<IReadOnlyList<ClanMemberInfo>> GetMembersAsync(long clanId, CancellationToken ct);
    Task<IReadOnlyList<ClanLeaderboardEntry>> GetTopAsync(long chatId, int limit, CancellationToken ct);
    Task ApplyGameCompletedAsync(GameCompletedMetaEvent ev, CancellationToken ct);
}

public sealed class ClanService(IMetaService meta, IClanStore clans) : IClanService
{
    public async Task<ClanCreateResult> CreateAsync(long chatId, long userId, string displayName, string tag, string name, CancellationToken ct)
    {
        var season = await meta.GetActiveSeasonAsync(ct);
        return await clans.CreateAsync(season, chatId, userId, displayName, tag, name, ct);
    }

    public async Task<ClanJoinResult> JoinAsync(long chatId, long userId, string displayName, string tag, CancellationToken ct)
    {
        var season = await meta.GetActiveSeasonAsync(ct);
        return await clans.JoinAsync(season, chatId, userId, displayName, tag, ct);
    }

    public async Task<ClanInfo?> GetUserClanAsync(long chatId, long userId, CancellationToken ct)
    {
        var season = await meta.GetActiveSeasonAsync(ct);
        return await clans.GetUserClanAsync(season, chatId, userId, ct);
    }

    public async Task<ClanInfo?> GetClanByTagAsync(long chatId, string tag, CancellationToken ct)
    {
        var season = await meta.GetActiveSeasonAsync(ct);
        return await clans.GetClanByTagAsync(season, chatId, tag, ct);
    }

    public Task<IReadOnlyList<ClanMemberInfo>> GetMembersAsync(long clanId, CancellationToken ct) =>
        clans.GetMembersAsync(clanId, ct);

    public async Task<IReadOnlyList<ClanLeaderboardEntry>> GetTopAsync(long chatId, int limit, CancellationToken ct)
    {
        var season = await meta.GetActiveSeasonAsync(ct);
        return await clans.GetTopAsync(season, chatId, limit, ct);
    }

    public async Task ApplyGameCompletedAsync(GameCompletedMetaEvent ev, CancellationToken ct)
    {
        var season = await meta.GetActiveSeasonAsync(ct);
        var xpDelta = CalculateClanXp(ev);
        await clans.ApplyGameCompletedAsync(season, ev, xpDelta, ct);
    }

    private static long CalculateClanXp(GameCompletedMetaEvent ev)
    {
        var baseXp = ev.IsWin ? 10 : 3;
        var stakeXp = (long)Math.Floor(Math.Max(0, ev.Stake) * 0.005m);
        return Math.Clamp(baseXp + stakeXp, 1, 250);
    }
}

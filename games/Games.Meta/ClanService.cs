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

public sealed class ClanService(
    IMetaService meta,
    IClanStore clans,
    IMetaHistoryStore history) : IClanService
{
    public async Task<ClanCreateResult> CreateAsync(long chatId, long userId, string displayName, string tag, string name, CancellationToken ct)
    {
        var season = await meta.GetActiveSeasonAsync(ct);
        var result = await clans.CreateAsync(season, chatId, userId, displayName, tag, name, ct);
        if (result.Created && result.Clan is not null)
        {
            await history.AppendAsync(
                "clan.created",
                "clan",
                result.Clan.Id.ToString(),
                season.Id,
                chatId,
                userId,
                new
                {
                    result.Clan.Id,
                    result.Clan.Tag,
                    result.Clan.Name,
                    result.Clan.OwnerUserId,
                    displayName,
                },
                ct);
        }
        return result;
    }

    public async Task<ClanJoinResult> JoinAsync(long chatId, long userId, string displayName, string tag, CancellationToken ct)
    {
        var season = await meta.GetActiveSeasonAsync(ct);
        var result = await clans.JoinAsync(season, chatId, userId, displayName, tag, ct);
        if (result.Joined && result.Clan is not null)
        {
            await history.AppendAsync(
                "clan.joined",
                "clan",
                result.Clan.Id.ToString(),
                season.Id,
                chatId,
                userId,
                new
                {
                    result.Clan.Id,
                    result.Clan.Tag,
                    result.Clan.Name,
                    displayName,
                    result.Clan.MemberCount,
                },
                ct);
        }
        return result;
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

        var clan = await clans.GetUserClanAsync(season, ev.ChatId, ev.UserId, ct);
        if (clan is not null)
        {
            await history.AppendAsync(
                "clan.progressed",
                "clan",
                clan.Id.ToString(),
                season.Id,
                ev.ChatId,
                ev.UserId,
                new
                {
                    clan.Id,
                    clan.Tag,
                    clan.Name,
                    xpDelta,
                    clan.SeasonXp,
                    clan.SeasonRating,
                    ev.GameKey,
                    ev.Stake,
                    ev.Payout,
                    ev.IsWin,
                },
                ct);
        }
    }

    private static long CalculateClanXp(GameCompletedMetaEvent ev)
    {
        var baseXp = ev.IsWin ? 10 : 3;
        var stakeXp = (long)Math.Floor(Math.Max(0, ev.Stake) * 0.005m);
        return Math.Clamp(baseXp + stakeXp, 1, 250);
    }
}

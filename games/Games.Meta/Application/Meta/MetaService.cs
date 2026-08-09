namespace Games.Meta.Application.Meta;

public sealed class MetaService(IMetaStore store, IMetaReadModel? reads = null) : IMetaService
{
    private IMetaReadModel Reads => reads ?? NullMetaReadModel.Instance;
    public async Task<MetaSeason> GetActiveSeasonAsync(CancellationToken ct)
    {
        var cached = await Reads.TryGetActiveSeasonAsync(ct);
        if (cached is not null) return cached;
        var season = await store.GetOrCreateActiveSeasonAsync(ct);
        await Reads.StoreActiveSeasonAsync(season, ct);
        return season;
    }

    public async Task<SeasonProfile> GetProfileAsync(long chatId, long userId, string displayName, CancellationToken ct)
    {
        var cached = await Reads.TryGetProfileAsync(chatId, userId, displayName, ct);
        if (cached is not null) return cached;
        var profile = await store.GetProfileAsync(chatId, userId, displayName, ct);
        await Reads.StoreActiveSeasonAsync(profile.Season, ct);
        await Reads.StoreProfileAsync(profile, ct);
        return profile;
    }

    public async Task<IReadOnlyList<SeasonLeaderboardEntry>> GetTopAsync(long chatId, int limit, CancellationToken ct)
    {
        if (!Reads.IsAvailable)
            return await store.GetTopAsync(chatId, limit, ct);
        var season = await GetActiveSeasonAsync(ct);
        var cached = await Reads.TryGetTopAsync(season.Id, chatId, limit, ct);
        if (cached is not null) return cached;
        var snapshot = await store.GetTopSnapshotAsync(chatId, ct);
        await Reads.HydrateTopAsync(season.Id, chatId, snapshot, ct);
        return snapshot.Take(Math.Clamp(limit, 1, 100)).ToArray();
    }

    public Task<IReadOnlyList<PlayerAchievementView>> GetAchievementsAsync(long chatId, long userId, CancellationToken ct) =>
        store.GetAchievementsAsync(chatId, userId, ct);

    public Task<GameStreakRecordResult?> RecordGamePlayedAsync(
        long seasonId,
        long chatId,
        long userId,
        string gameKey,
        DateOnly playedOn,
        CancellationToken ct) =>
        store.RecordGamePlayedAsync(seasonId, chatId, userId, gameKey, playedOn, ct);

    public Task<IReadOnlyList<PlayerGameStreakView>> GetGameStreaksAsync(
        long chatId,
        long userId,
        CancellationToken ct) =>
        store.GetGameStreaksAsync(chatId, userId, ct);

    public async Task<SeasonPlayer> ApplyGameCompletedAsync(
        long chatId,
        long userId,
        string displayName,
        long stake,
        long payout,
        bool isWin,
        CancellationToken ct)
    {
        var player = await store.ApplyGameCompletedAsync(chatId, userId, displayName, stake, payout, isWin, ct);
        await Reads.UpsertPlayerAsync(player, ct);
        return player;
    }

    public async Task<SeasonPlayer> AddSeasonXpAsync(
        long seasonId,
        long chatId,
        long userId,
        string displayName,
        long xpDelta,
        CancellationToken ct)
    {
        var player = await store.AddSeasonXpAsync(seasonId, chatId, userId, displayName, xpDelta, ct);
        await Reads.UpsertPlayerAsync(player, ct);
        return player;
    }

    public Task<IReadOnlyList<AchievementUnlock>> UnlockAchievementsAsync(
        long seasonId,
        long chatId,
        long userId,
        IEnumerable<AchievementDefinition> achievements,
        CancellationToken ct) =>
        store.UnlockAchievementsAsync(seasonId, chatId, userId, achievements, ct);
}

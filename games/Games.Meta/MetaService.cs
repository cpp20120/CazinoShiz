namespace Games.Meta;

public interface IMetaService
{
    Task<MetaSeason> GetActiveSeasonAsync(CancellationToken ct);
    Task<SeasonProfile> GetProfileAsync(long chatId, long userId, string displayName, CancellationToken ct);
    Task<IReadOnlyList<SeasonLeaderboardEntry>> GetTopAsync(long chatId, int limit, CancellationToken ct);
    Task<IReadOnlyList<PlayerAchievementView>> GetAchievementsAsync(long chatId, long userId, CancellationToken ct);
    Task<SeasonPlayer> ApplyGameCompletedAsync(
        long chatId,
        long userId,
        string displayName,
        long stake,
        long payout,
        bool isWin,
        CancellationToken ct);
    Task<SeasonPlayer> AddSeasonXpAsync(
        long seasonId,
        long chatId,
        long userId,
        string displayName,
        long xpDelta,
        CancellationToken ct);
    Task<IReadOnlyList<AchievementUnlock>> UnlockAchievementsAsync(
        long seasonId,
        long chatId,
        long userId,
        IEnumerable<AchievementDefinition> achievements,
        CancellationToken ct);
}

public sealed class MetaService(IMetaStore store) : IMetaService
{
    public Task<MetaSeason> GetActiveSeasonAsync(CancellationToken ct) =>
        store.GetOrCreateActiveSeasonAsync(ct);

    public Task<SeasonProfile> GetProfileAsync(long chatId, long userId, string displayName, CancellationToken ct) =>
        store.GetProfileAsync(chatId, userId, displayName, ct);

    public Task<IReadOnlyList<SeasonLeaderboardEntry>> GetTopAsync(long chatId, int limit, CancellationToken ct) =>
        store.GetTopAsync(chatId, limit, ct);

    public Task<IReadOnlyList<PlayerAchievementView>> GetAchievementsAsync(long chatId, long userId, CancellationToken ct) =>
        store.GetAchievementsAsync(chatId, userId, ct);

    public Task<SeasonPlayer> ApplyGameCompletedAsync(
        long chatId,
        long userId,
        string displayName,
        long stake,
        long payout,
        bool isWin,
        CancellationToken ct) =>
        store.ApplyGameCompletedAsync(chatId, userId, displayName, stake, payout, isWin, ct);

    public Task<SeasonPlayer> AddSeasonXpAsync(
        long seasonId,
        long chatId,
        long userId,
        string displayName,
        long xpDelta,
        CancellationToken ct) =>
        store.AddSeasonXpAsync(seasonId, chatId, userId, displayName, xpDelta, ct);

    public Task<IReadOnlyList<AchievementUnlock>> UnlockAchievementsAsync(
        long seasonId,
        long chatId,
        long userId,
        IEnumerable<AchievementDefinition> achievements,
        CancellationToken ct) =>
        store.UnlockAchievementsAsync(seasonId, chatId, userId, achievements, ct);
}

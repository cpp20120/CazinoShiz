namespace Games.Meta;

public interface IMetaService
{
    Task<MetaSeason> GetActiveSeasonAsync(CancellationToken ct);
    Task<SeasonProfile> GetProfileAsync(long chatId, long userId, string displayName, CancellationToken ct);
    Task<IReadOnlyList<SeasonLeaderboardEntry>> GetTopAsync(long chatId, int limit, CancellationToken ct);
    Task<SeasonPlayer> ApplyGameCompletedAsync(
        long chatId,
        long userId,
        string displayName,
        long stake,
        long payout,
        bool isWin,
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

    public Task<SeasonPlayer> ApplyGameCompletedAsync(
        long chatId,
        long userId,
        string displayName,
        long stake,
        long payout,
        bool isWin,
        CancellationToken ct) =>
        store.ApplyGameCompletedAsync(chatId, userId, displayName, stake, payout, isWin, ct);
}

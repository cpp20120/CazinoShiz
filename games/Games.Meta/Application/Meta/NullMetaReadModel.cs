namespace Games.Meta.Application.Meta;

internal sealed class NullMetaReadModel : IMetaReadModel
{
    public static NullMetaReadModel Instance { get; } = new();
    public Task<MetaSeason?> TryGetActiveSeasonAsync(CancellationToken ct) => Task.FromResult<MetaSeason?>(null);
    public Task StoreActiveSeasonAsync(MetaSeason season, CancellationToken ct) => Task.CompletedTask;
    public Task<SeasonProfile?> TryGetProfileAsync(long chatId, long userId, string displayName, CancellationToken ct) => Task.FromResult<SeasonProfile?>(null);
    public Task StoreProfileAsync(SeasonProfile profile, CancellationToken ct) => Task.CompletedTask;
    public Task<IReadOnlyList<SeasonLeaderboardEntry>?> TryGetTopAsync(long seasonId, long chatId, int limit, CancellationToken ct) => Task.FromResult<IReadOnlyList<SeasonLeaderboardEntry>?>(null);
    public Task HydrateTopAsync(long seasonId, long chatId, IReadOnlyList<SeasonLeaderboardEntry> entries, CancellationToken ct) => Task.CompletedTask;
    public Task UpsertPlayerAsync(SeasonPlayer player, CancellationToken ct) => Task.CompletedTask;
}

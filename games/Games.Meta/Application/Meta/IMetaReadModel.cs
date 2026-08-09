namespace Games.Meta.Application.Meta;

/// <summary>Non-authoritative, tenant-scoped Meta read model. Cache misses must fall back to PostgreSQL.</summary>
public interface IMetaReadModel
{
    bool IsAvailable => false;
    Task<MetaSeason?> TryGetActiveSeasonAsync(CancellationToken ct);
    Task StoreActiveSeasonAsync(MetaSeason season, CancellationToken ct);
    Task<SeasonProfile?> TryGetProfileAsync(long chatId, long userId, string displayName, CancellationToken ct);
    Task StoreProfileAsync(SeasonProfile profile, CancellationToken ct);
    Task<IReadOnlyList<SeasonLeaderboardEntry>?> TryGetTopAsync(long seasonId, long chatId, int limit, CancellationToken ct);
    Task HydrateTopAsync(long seasonId, long chatId, IReadOnlyList<SeasonLeaderboardEntry> entries, CancellationToken ct);
    Task UpsertPlayerAsync(SeasonPlayer player, CancellationToken ct);
}

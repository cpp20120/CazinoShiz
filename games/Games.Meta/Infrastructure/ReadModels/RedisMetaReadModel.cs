using System.Globalization;
using System.Text.Json;
using BotFramework.Contracts.Messaging;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace Games.Meta.Infrastructure.ReadModels;

/// <summary>
/// Redis projection for Meta's non-authoritative reads. Every key is namespaced
/// by the current tenant and scope; a miss or Redis failure always falls back
/// to the RLS-protected PostgreSQL store.
/// </summary>
public sealed partial class RedisMetaReadModel(
    IServiceProvider services,
    ILogger<RedisMetaReadModel> logger) : IMetaReadModel
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan SeasonTtl = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ProfileTtl = TimeSpan.FromMinutes(5);

    public bool IsAvailable => Database is not null;

    public async Task<MetaSeason?> TryGetActiveSeasonAsync(CancellationToken ct)
    {
        var value = await GetStringAsync(SeasonKey(), ct);
        return string.IsNullOrEmpty(value) ? null : Deserialize<MetaSeason>(value);
    }

    public Task StoreActiveSeasonAsync(MetaSeason season, CancellationToken ct) =>
        SetStringAsync(SeasonKey(), JsonSerializer.Serialize(season, Json), SeasonTtl, ct);

    public async Task<SeasonProfile?> TryGetProfileAsync(long chatId, long userId, string displayName, CancellationToken ct)
    {
        var value = await GetStringAsync(ProfileKey(chatId, userId), ct);
        var profile = string.IsNullOrEmpty(value) ? null : Deserialize<SeasonProfile>(value);
        return profile is not null && string.Equals(profile.Player.DisplayName, displayName, StringComparison.Ordinal)
            ? profile : null;
    }

    public Task StoreProfileAsync(SeasonProfile profile, CancellationToken ct) =>
        SetStringAsync(ProfileKey(profile.Player.ChatId, profile.Player.UserId), JsonSerializer.Serialize(profile, Json), ProfileTtl, ct);

    public async Task<IReadOnlyList<SeasonLeaderboardEntry>?> TryGetTopAsync(long seasonId, long chatId, int limit, CancellationToken ct)
    {
        var db = Database;
        if (db is null || !await ExistsAsync(db, ReadyKey(seasonId, chatId), ct)) return null;
        try
        {
            // Scores index XP; player payloads retain the complete ordering
            // tuple, so ties preserve the PostgreSQL semantics exactly.
            var members = await db.SortedSetRangeByRankAsync(SortedSetKey(seasonId, chatId), 0, -1, Order.Descending);
            var values = await Task.WhenAll(members.Select(member => db.StringGetAsync(PlayerKey(seasonId, chatId, member))));
            var rows = new List<CachedPlayer>(values.Length);
            foreach (var value in values)
            {
                if (!value.HasValue || Deserialize<CachedPlayer>(value) is not { } row) return null;
                rows.Add(row);
            }
            return rows
                .OrderByDescending(static row => row.Xp)
                .ThenByDescending(static row => row.Rating)
                .ThenBy(static row => row.UserId)
                .Take(Math.Clamp(limit, 1, 100))
                .Select((row, index) => row.ToEntry(index + 1))
                .ToArray();
        }
        catch (RedisException exception)
        {
            LogReadFailed(exception);
            return null;
        }
    }

    public async Task HydrateTopAsync(long seasonId, long chatId, IReadOnlyList<SeasonLeaderboardEntry> entries, CancellationToken ct)
    {
        var db = Database;
        if (db is null) return;
        try
        {
            var batch = db.CreateBatch();
            var tasks = new List<Task>(entries.Count * 2 + 1);
            foreach (var entry in entries)
            {
                var cached = CachedPlayer.From(entry);
                tasks.Add(batch.StringSetAsync(PlayerKey(seasonId, chatId, Member(entry.UserId)), JsonSerializer.Serialize(cached, Json)));
                tasks.Add(batch.SortedSetAddAsync(SortedSetKey(seasonId, chatId), Member(entry.UserId), entry.Xp));
            }
            tasks.Add(batch.StringSetAsync(ReadyKey(seasonId, chatId), "1"));
            batch.Execute();
            await Task.WhenAll(tasks).WaitAsync(ct);
        }
        catch (RedisException exception) { LogWriteFailed(exception); }
    }

    public async Task UpsertPlayerAsync(SeasonPlayer player, CancellationToken ct)
    {
        var db = Database;
        if (db is null) return;
        try
        {
            var member = Member(player.UserId);
            var cached = CachedPlayer.From(player);
            var batch = db.CreateBatch();
            var write = batch.StringSetAsync(PlayerKey(player.SeasonId, player.ChatId, member), JsonSerializer.Serialize(cached, Json));
            var rank = batch.SortedSetAddAsync(SortedSetKey(player.SeasonId, player.ChatId), member, player.Xp);
            var removeProfile = batch.KeyDeleteAsync(ProfileKey(player.ChatId, player.UserId));
            batch.Execute();
            await Task.WhenAll(write, rank, removeProfile).WaitAsync(ct);
        }
        catch (RedisException exception) { LogWriteFailed(exception); }
    }

    // A nullable constructor parameter is still required by Microsoft DI. Resolve
    // Redis lazily instead so this optional read model remains a harmless no-op
    // in hosts that deliberately run without Redis (tests, local tools).
    private IDatabase? Database => services.GetService<IConnectionMultiplexer>() is { IsConnected: true } redis
        ? redis.GetDatabase()
        : null;
    private async Task<string?> GetStringAsync(RedisKey key, CancellationToken ct)
    {
        var db = Database;
        if (db is null) return null;
        try { return await db.StringGetAsync(key).WaitAsync(ct); }
        catch (RedisException exception) { LogReadFailed(exception); return null; }
    }
    private async Task SetStringAsync(RedisKey key, string value, TimeSpan ttl, CancellationToken ct)
    {
        var db = Database;
        if (db is null) return;
        try { await db.StringSetAsync(key, value, ttl).WaitAsync(ct); }
        catch (RedisException exception) { LogWriteFailed(exception); }
    }
    private static async Task<bool> ExistsAsync(IDatabase db, RedisKey key, CancellationToken ct) => await db.KeyExistsAsync(key).WaitAsync(ct);
    private static T? Deserialize<T>(RedisValue value) where T : class { try { return JsonSerializer.Deserialize<T>(value.ToString(), Json); } catch (JsonException) { return null; } }
    private static string Member(long userId) => userId.ToString("D20", CultureInfo.InvariantCulture);
    private static string Prefix()
    {
        var context = RequestMetadataContext.TryGetCurrent()?.TenantContext;
        return context is null ? "meta:read:v1:unbound" : string.Create(CultureInfo.InvariantCulture, $"meta:read:v1:{context.TenantId.Value}:{context.ScopeId.Value}");
    }
    private static RedisKey SeasonKey() => $"{Prefix()}:season";
    private static RedisKey ProfileKey(long chatId, long userId) => $"{Prefix()}:profile:{chatId}:{userId}";
    private static RedisKey SortedSetKey(long seasonId, long chatId) => $"{Prefix()}:season:{seasonId}:chat:{chatId}:top";
    private static RedisKey ReadyKey(long seasonId, long chatId) => $"{Prefix()}:season:{seasonId}:chat:{chatId}:top:ready";
    private static RedisKey PlayerKey(long seasonId, long chatId, RedisValue member) => $"{Prefix()}:season:{seasonId}:chat:{chatId}:player:{member}";
    private sealed record CachedPlayer(long UserId, string DisplayName, long Xp, int Level, int Rating, int GamesPlayed, int Wins, int Losses)
    {
        public static CachedPlayer From(SeasonLeaderboardEntry row) => new(row.UserId, row.DisplayName, row.Xp, row.Level, row.Rating, row.GamesPlayed, row.Wins, row.Losses);
        public static CachedPlayer From(SeasonPlayer row) => new(row.UserId, row.DisplayName, row.Xp, row.Level, row.Rating, row.GamesPlayed, row.Wins, row.Losses);
        public SeasonLeaderboardEntry ToEntry(int place) => new(place, UserId, DisplayName, Xp, Level, Rating, GamesPlayed, Wins, Losses);
    }
    [LoggerMessage(LogLevel.Debug, "meta.redis_read_failed")] private partial void LogReadFailed(Exception exception);
    [LoggerMessage(LogLevel.Debug, "meta.redis_write_failed")] private partial void LogWriteFailed(Exception exception);
}

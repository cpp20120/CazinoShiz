using BotFramework.Host;
using Dapper;

namespace Games.Leaderboard;

public interface ILeaderboardStore
{
    Task<IReadOnlyList<LeaderboardUser>> ListActiveAsync(long sinceUnixMs, long balanceScopeId, CancellationToken ct);
    Task<(int Coins, long UpdatedAtUnixMs)?> FindAsync(long userId, long balanceScopeId, CancellationToken ct);
    Task<(IReadOnlyList<GlobalLeaderboardUser> Users, int TotalUsers)> ListGlobalAggregateAsync(
        long sinceUnixMs, int limit, CancellationToken ct);
    Task<IReadOnlyList<(long ChatId, string? Title, string ChatType, LeaderboardUser User)>> ListGlobalSplitAsync(
        long sinceUnixMs, int perChatLimit, CancellationToken ct);
}

public sealed class LeaderboardStore(INpgsqlConnectionFactory connections) : ILeaderboardStore
{
    public async Task<IReadOnlyList<LeaderboardUser>> ListActiveAsync(
        long sinceUnixMs, long balanceScopeId, CancellationToken ct)
    {
        const string sql = """
            SELECT telegram_user_id AS TelegramUserId,
                   balance_scope_id AS BalanceScopeId,
                   display_name     AS DisplayName,
                   coins            AS Coins,
                   (EXTRACT(EPOCH FROM updated_at) * 1000)::BIGINT AS UpdatedAtUnixMs
            FROM users
            WHERE balance_scope_id = @balanceScopeId
              AND (EXTRACT(EPOCH FROM updated_at) * 1000)::BIGINT >= @sinceUnixMs
            ORDER BY coins DESC
            """;

        await using var conn = await connections.OpenAsync(ct);
        var rows = await conn.QueryAsync<LeaderboardUser>(new CommandDefinition(
            sql, new { sinceUnixMs, balanceScopeId }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<(int Coins, long UpdatedAtUnixMs)?> FindAsync(
        long userId, long balanceScopeId, CancellationToken ct)
    {
        const string sql = """
            SELECT coins AS Coins,
                   (EXTRACT(EPOCH FROM updated_at) * 1000)::BIGINT AS UpdatedAtUnixMs
            FROM users
            WHERE telegram_user_id = @userId AND balance_scope_id = @balanceScopeId
            """;

        await using var conn = await connections.OpenAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<(int Coins, long UpdatedAtUnixMs)?>(new CommandDefinition(
            sql, new { userId, balanceScopeId }, cancellationToken: ct));
        return row;
    }

    public async Task<(IReadOnlyList<GlobalLeaderboardUser> Users, int TotalUsers)> ListGlobalAggregateAsync(
        long sinceUnixMs, int limit, CancellationToken ct)
    {
        const string aggregateSql = """
            WITH active AS (
                SELECT u.telegram_user_id,
                       u.display_name,
                       u.coins,
                       u.updated_at,
                       row_number() OVER (
                           PARTITION BY u.telegram_user_id
                           ORDER BY u.updated_at DESC
                       ) AS rn
                FROM users u
                WHERE (EXTRACT(EPOCH FROM u.updated_at) * 1000)::BIGINT >= @sinceUnixMs
            ),
            latest_name AS (
                SELECT telegram_user_id, display_name
                FROM active
                WHERE rn = 1
            )
            SELECT a.telegram_user_id     AS TelegramUserId,
                   ln.display_name        AS DisplayName,
                   SUM(a.coins)::INT      AS TotalCoins,
                   COUNT(*)::INT          AS ChatCount
            FROM active a
            JOIN latest_name ln USING (telegram_user_id)
            GROUP BY a.telegram_user_id, ln.display_name
            ORDER BY TotalCoins DESC, a.telegram_user_id ASC
            """;

        await using var conn = await connections.OpenAsync(ct);
        var all = (await conn.QueryAsync<GlobalLeaderboardUser>(new CommandDefinition(
            aggregateSql, new { sinceUnixMs }, cancellationToken: ct))).ToList();

        if (limit > 0 && all.Count > limit)
            return (all.Take(limit).ToList(), all.Count);

        return (all, all.Count);
    }

    public async Task<IReadOnlyList<(long ChatId, string? Title, string ChatType, LeaderboardUser User)>>
        ListGlobalSplitAsync(long sinceUnixMs, int perChatLimit, CancellationToken ct)
    {
        // Per-chat top using ROW_NUMBER over balance_scope_id; LEFT JOIN known_chats
        // for human-readable title.
        var sql = $"""
            WITH ranked AS (
                SELECT u.telegram_user_id,
                       u.balance_scope_id,
                       u.display_name,
                       u.coins,
                       (EXTRACT(EPOCH FROM u.updated_at) * 1000)::BIGINT AS updated_at_ms,
                       row_number() OVER (
                           PARTITION BY u.balance_scope_id
                           ORDER BY u.coins DESC, u.telegram_user_id ASC
                       ) AS rn
                FROM users u
                WHERE (EXTRACT(EPOCH FROM u.updated_at) * 1000)::BIGINT >= @sinceUnixMs
            )
            SELECT r.balance_scope_id      AS ChatId,
                   kc.title                AS Title,
                   COALESCE(kc.chat_type, 'unknown') AS ChatType,
                   r.telegram_user_id      AS TelegramUserId,
                   r.balance_scope_id      AS BalanceScopeId,
                   r.display_name          AS DisplayName,
                   r.coins                 AS Coins,
                   r.updated_at_ms         AS UpdatedAtUnixMs
            FROM ranked r
            LEFT JOIN known_chats kc ON kc.chat_id = r.balance_scope_id
            WHERE @perChatLimit <= 0 OR r.rn <= @perChatLimit
            ORDER BY r.balance_scope_id ASC, r.coins DESC, r.telegram_user_id ASC
            """;

        await using var conn = await connections.OpenAsync(ct);
        var rows = await conn.QueryAsync<SplitRow>(new CommandDefinition(
            sql, new { sinceUnixMs, perChatLimit }, cancellationToken: ct));

        return rows.Select(r => (
            r.ChatId,
            r.Title,
            r.ChatType,
            new LeaderboardUser(r.TelegramUserId, r.BalanceScopeId, r.DisplayName, r.Coins, r.UpdatedAtUnixMs)
        )).ToList();
    }

    private sealed record SplitRow(
        long ChatId,
        string? Title,
        string ChatType,
        long TelegramUserId,
        long BalanceScopeId,
        string DisplayName,
        int Coins,
        long UpdatedAtUnixMs);
}

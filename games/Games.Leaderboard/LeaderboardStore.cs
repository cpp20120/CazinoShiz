using BotFramework.Host;
using Dapper;

namespace Games.Leaderboard;

public interface ILeaderboardStore
{
    Task<IReadOnlyList<LeaderboardUser>> ListActiveAsync(long sinceUnixMs, CancellationToken ct);
    Task<(int Coins, long UpdatedAtUnixMs)?> FindAsync(long userId, CancellationToken ct);
}

public sealed class LeaderboardStore(INpgsqlConnectionFactory connections) : ILeaderboardStore
{
    public async Task<IReadOnlyList<LeaderboardUser>> ListActiveAsync(long sinceUnixMs, CancellationToken ct)
    {
        const string sql = """
            SELECT telegram_user_id AS TelegramUserId,
                   display_name     AS DisplayName,
                   coins            AS Coins,
                   (EXTRACT(EPOCH FROM updated_at) * 1000)::BIGINT AS UpdatedAtUnixMs
            FROM users
            WHERE (EXTRACT(EPOCH FROM updated_at) * 1000)::BIGINT >= @sinceUnixMs
            ORDER BY coins DESC
            """;

        await using var conn = await connections.OpenAsync(ct);
        var rows = await conn.QueryAsync<LeaderboardUser>(new CommandDefinition(
            sql, new { sinceUnixMs }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<(int Coins, long UpdatedAtUnixMs)?> FindAsync(long userId, CancellationToken ct)
    {
        const string sql = """
            SELECT coins AS Coins,
                   (EXTRACT(EPOCH FROM updated_at) * 1000)::BIGINT AS UpdatedAtUnixMs
            FROM users WHERE telegram_user_id = @userId
            """;

        await using var conn = await connections.OpenAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<(int Coins, long UpdatedAtUnixMs)?>(new CommandDefinition(
            sql, new { userId }, cancellationToken: ct));
        return row;
    }
}

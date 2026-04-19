using BotFramework.Host;
using Dapper;

namespace Games.Admin;

public interface IAdminStore
{
    Task<IReadOnlyList<UserSummary>> ListUsersAsync(CancellationToken ct);
    Task<UserSummary?> FindUserAsync(long userId, CancellationToken ct);

    Task<string?> GetOverrideAsync(string originalName, CancellationToken ct);
    Task UpsertOverrideAsync(string originalName, string newName, CancellationToken ct);
    Task<bool> DeleteOverrideAsync(string originalName, CancellationToken ct);
}

public sealed class AdminStore(INpgsqlConnectionFactory connections) : IAdminStore
{
    public async Task<IReadOnlyList<UserSummary>> ListUsersAsync(CancellationToken ct)
    {
        const string sql = """
            SELECT telegram_user_id AS TelegramUserId,
                   display_name     AS DisplayName,
                   coins            AS Coins,
                   (EXTRACT(EPOCH FROM updated_at) * 1000)::BIGINT AS UpdatedAtUnixMs
            FROM users ORDER BY coins DESC
            """;

        await using var conn = await connections.OpenAsync(ct);
        var rows = await conn.QueryAsync<UserSummary>(new CommandDefinition(sql, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<UserSummary?> FindUserAsync(long userId, CancellationToken ct)
    {
        const string sql = """
            SELECT telegram_user_id AS TelegramUserId,
                   display_name     AS DisplayName,
                   coins            AS Coins,
                   (EXTRACT(EPOCH FROM updated_at) * 1000)::BIGINT AS UpdatedAtUnixMs
            FROM users WHERE telegram_user_id = @userId
            """;

        await using var conn = await connections.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<UserSummary>(
            new CommandDefinition(sql, new { userId }, cancellationToken: ct));
    }

    public async Task<string?> GetOverrideAsync(string originalName, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT new_name FROM display_name_overrides WHERE original_name = @originalName",
            new { originalName }, cancellationToken: ct));
    }

    public async Task UpsertOverrideAsync(string originalName, string newName, CancellationToken ct)
    {
        const string sql = """
            INSERT INTO display_name_overrides (original_name, new_name)
            VALUES (@originalName, @newName)
            ON CONFLICT (original_name) DO UPDATE SET new_name = EXCLUDED.new_name
            """;
        await using var conn = await connections.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(sql,
            new { originalName, newName }, cancellationToken: ct));
    }

    public async Task<bool> DeleteOverrideAsync(string originalName, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        var rows = await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM display_name_overrides WHERE original_name = @originalName",
            new { originalName }, cancellationToken: ct));
        return rows > 0;
    }
}

using BotFramework.Host;
using Dapper;

namespace Games.Admin;

public interface IAdminStore
{
    Task<IReadOnlyList<UserSummary>> ListUsersAsync(CancellationToken ct);
    Task<UserSummary?> FindUserAsync(long userId, long balanceScopeId, CancellationToken ct);

    Task<IReadOnlyList<PendingChatBet>> DeletePendingMiniGameBetsAsync(long chatId, CancellationToken ct);

    Task<string?> GetOverrideAsync(string originalName, CancellationToken ct);
    Task UpsertOverrideAsync(string originalName, string newName, CancellationToken ct);
    Task<bool> DeleteOverrideAsync(string originalName, CancellationToken ct);
}

public sealed class PendingChatBet
{
    public string GameId { get; init; } = "";
    public long UserId { get; init; }
    public long ChatId { get; init; }
    public int Amount { get; init; }
    public int? BotMessageId { get; init; }
}

public sealed class AdminStore(INpgsqlConnectionFactory connections) : IAdminStore
{
    public async Task<IReadOnlyList<UserSummary>> ListUsersAsync(CancellationToken ct)
    {
        const string sql = """
            SELECT telegram_user_id AS TelegramUserId,
                   balance_scope_id AS BalanceScopeId,
                   display_name     AS DisplayName,
                   coins            AS Coins,
                   (EXTRACT(EPOCH FROM updated_at) * 1000)::BIGINT AS UpdatedAtUnixMs
            FROM users ORDER BY coins DESC
            """;

        await using var conn = await connections.OpenAsync(ct);
        var rows = await conn.QueryAsync<UserSummary>(new CommandDefinition(sql, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<UserSummary?> FindUserAsync(long userId, long balanceScopeId, CancellationToken ct)
    {
        const string sql = """
            SELECT telegram_user_id AS TelegramUserId,
                   balance_scope_id AS BalanceScopeId,
                   display_name     AS DisplayName,
                   coins            AS Coins,
                   (EXTRACT(EPOCH FROM updated_at) * 1000)::BIGINT AS UpdatedAtUnixMs
            FROM users WHERE telegram_user_id = @userId AND balance_scope_id = @balanceScopeId
            """;

        await using var conn = await connections.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<UserSummary>(
            new CommandDefinition(sql, new { userId, balanceScopeId }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<PendingChatBet>> DeletePendingMiniGameBetsAsync(long chatId, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var deleted = new List<PendingChatBet>();
        deleted.AddRange(await conn.QueryAsync<PendingChatBet>(new CommandDefinition("""
            DELETE FROM dicecube_bets
            WHERE chat_id = @chatId
            RETURNING 'dicecube' AS GameId, user_id AS UserId, chat_id AS ChatId, amount AS Amount, NULL::integer AS BotMessageId
            """,
            new { chatId }, transaction: tx, cancellationToken: ct)));
        deleted.AddRange(await conn.QueryAsync<PendingChatBet>(new CommandDefinition("""
            DELETE FROM football_bets
            WHERE chat_id = @chatId
            RETURNING 'football' AS GameId, user_id AS UserId, chat_id AS ChatId, amount AS Amount, NULL::integer AS BotMessageId
            """,
            new { chatId }, transaction: tx, cancellationToken: ct)));
        deleted.AddRange(await conn.QueryAsync<PendingChatBet>(new CommandDefinition("""
            DELETE FROM basketball_bets
            WHERE chat_id = @chatId
            RETURNING 'basketball' AS GameId, user_id AS UserId, chat_id AS ChatId, amount AS Amount, NULL::integer AS BotMessageId
            """,
            new { chatId }, transaction: tx, cancellationToken: ct)));
        deleted.AddRange(await conn.QueryAsync<PendingChatBet>(new CommandDefinition("""
            DELETE FROM bowling_bets
            WHERE chat_id = @chatId
            RETURNING 'bowling' AS GameId, user_id AS UserId, chat_id AS ChatId, amount AS Amount, NULL::integer AS BotMessageId
            """,
            new { chatId }, transaction: tx, cancellationToken: ct)));
        deleted.AddRange(await conn.QueryAsync<PendingChatBet>(new CommandDefinition("""
            DELETE FROM darts_rounds
            WHERE chat_id = @chatId AND status IN (@queued, @awaiting)
            RETURNING 'darts' AS GameId, user_id AS UserId, chat_id AS ChatId, amount AS Amount, bot_message_id AS BotMessageId
            """,
            new
            {
                chatId,
                queued = (short)Games.Darts.DartsRoundStatus.Queued,
                awaiting = (short)Games.Darts.DartsRoundStatus.AwaitingOutcome,
            },
            transaction: tx, cancellationToken: ct)));

        await tx.CommitAsync(ct);
        return deleted;
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

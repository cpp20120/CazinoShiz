using BotFramework.Host;
using Dapper;

namespace Games.Darts;

public interface IDartsBetStore
{
    Task<DartsBet?> FindAsync(long userId, long chatId, CancellationToken ct);
    Task InsertAsync(DartsBet bet, CancellationToken ct);
    Task DeleteAsync(long userId, long chatId, CancellationToken ct);
}

public sealed class DartsBetStore(INpgsqlConnectionFactory connections) : IDartsBetStore
{
    public async Task<DartsBet?> FindAsync(long userId, long chatId, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<DartsBet>(new CommandDefinition("""
            SELECT user_id AS UserId, chat_id AS ChatId, amount AS Amount, created_at AS CreatedAt
            FROM darts_bets WHERE user_id = @userId AND chat_id = @chatId
            """,
            new { userId, chatId },
            cancellationToken: ct));
    }

    public async Task InsertAsync(DartsBet bet, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition("""
            INSERT INTO darts_bets (user_id, chat_id, amount, created_at)
            VALUES (@UserId, @ChatId, @Amount, @CreatedAt)
            """,
            bet,
            cancellationToken: ct));
    }

    public async Task DeleteAsync(long userId, long chatId, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM darts_bets WHERE user_id = @userId AND chat_id = @chatId",
            new { userId, chatId },
            cancellationToken: ct));
    }
}

public sealed record DartsBet(long UserId, long ChatId, int Amount, DateTimeOffset CreatedAt);

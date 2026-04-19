using BotFramework.Host;
using Dapper;

namespace Games.Bowling;

public interface IBowlingBetStore
{
    Task<BowlingBet?> FindAsync(long userId, long chatId, CancellationToken ct);
    Task InsertAsync(BowlingBet bet, CancellationToken ct);
    Task DeleteAsync(long userId, long chatId, CancellationToken ct);
}

public sealed class BowlingBetStore(INpgsqlConnectionFactory connections) : IBowlingBetStore
{
    public async Task<BowlingBet?> FindAsync(long userId, long chatId, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<BowlingBet>(new CommandDefinition("""
            SELECT user_id AS UserId, chat_id AS ChatId, amount AS Amount, created_at AS CreatedAt
            FROM bowling_bets WHERE user_id = @userId AND chat_id = @chatId
            """,
            new { userId, chatId },
            cancellationToken: ct));
    }

    public async Task InsertAsync(BowlingBet bet, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition("""
            INSERT INTO bowling_bets (user_id, chat_id, amount, created_at)
            VALUES (@UserId, @ChatId, @Amount, @CreatedAt)
            """,
            bet,
            cancellationToken: ct));
    }

    public async Task DeleteAsync(long userId, long chatId, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM bowling_bets WHERE user_id = @userId AND chat_id = @chatId",
            new { userId, chatId },
            cancellationToken: ct));
    }
}

public sealed record BowlingBet(long UserId, long ChatId, int Amount, DateTimeOffset CreatedAt);

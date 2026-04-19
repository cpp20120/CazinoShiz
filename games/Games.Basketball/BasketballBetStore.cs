using BotFramework.Host;
using Dapper;

namespace Games.Basketball;

public interface IBasketballBetStore
{
    Task<BasketballBet?> FindAsync(long userId, long chatId, CancellationToken ct);
    Task InsertAsync(BasketballBet bet, CancellationToken ct);
    Task DeleteAsync(long userId, long chatId, CancellationToken ct);
}

public sealed class BasketballBetStore(INpgsqlConnectionFactory connections) : IBasketballBetStore
{
    public async Task<BasketballBet?> FindAsync(long userId, long chatId, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<BasketballBet>(new CommandDefinition("""
            SELECT user_id AS UserId, chat_id AS ChatId, amount AS Amount, created_at AS CreatedAt
            FROM basketball_bets WHERE user_id = @userId AND chat_id = @chatId
            """,
            new { userId, chatId },
            cancellationToken: ct));
    }

    public async Task InsertAsync(BasketballBet bet, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition("""
            INSERT INTO basketball_bets (user_id, chat_id, amount, created_at)
            VALUES (@UserId, @ChatId, @Amount, @CreatedAt)
            """,
            bet,
            cancellationToken: ct));
    }

    public async Task DeleteAsync(long userId, long chatId, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM basketball_bets WHERE user_id = @userId AND chat_id = @chatId",
            new { userId, chatId },
            cancellationToken: ct));
    }
}

public sealed record BasketballBet(long UserId, long ChatId, int Amount, DateTimeOffset CreatedAt);

using BotFramework.Host;
using Dapper;

namespace Games.Football;

public interface IFootballBetStore
{
    Task<FootballBet?> FindAsync(long userId, long chatId, CancellationToken ct);
    Task<bool> InsertAsync(FootballBet bet, CancellationToken ct);
    Task DeleteAsync(long userId, long chatId, CancellationToken ct);
}

public sealed class FootballBetStore(INpgsqlConnectionFactory connections) : IFootballBetStore
{
    public async Task<FootballBet?> FindAsync(long userId, long chatId, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<FootballBet>(new CommandDefinition("""
            SELECT user_id AS UserId, chat_id AS ChatId, amount AS Amount, created_at AS CreatedAt
            FROM football_bets WHERE user_id = @userId AND chat_id = @chatId
            """,
            new { userId, chatId },
            cancellationToken: ct));
    }

    public async Task<bool> InsertAsync(FootballBet bet, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        var rows = await conn.ExecuteAsync(new CommandDefinition("""
            INSERT INTO football_bets (user_id, chat_id, amount, created_at)
            VALUES (@UserId, @ChatId, @Amount, @CreatedAt)
            ON CONFLICT (user_id, chat_id) DO NOTHING
            """,
            bet,
            cancellationToken: ct));
        return rows > 0;
    }

    public async Task DeleteAsync(long userId, long chatId, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM football_bets WHERE user_id = @userId AND chat_id = @chatId",
            new { userId, chatId },
            cancellationToken: ct));
    }
}

public sealed record FootballBet(long UserId, long ChatId, int Amount, DateTimeOffset CreatedAt);

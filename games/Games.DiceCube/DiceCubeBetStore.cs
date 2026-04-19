// ─────────────────────────────────────────────────────────────────────────────
// DiceCubeBetStore — Dapper-backed persistence for pending cube bets. A bet
// is keyed by (user_id, chat_id) so the same user can have independent bets
// in multiple group chats.
// ─────────────────────────────────────────────────────────────────────────────

using BotFramework.Host;
using Dapper;

namespace Games.DiceCube;

public interface IDiceCubeBetStore
{
    Task<DiceCubeBet?> FindAsync(long userId, long chatId, CancellationToken ct);
    Task InsertAsync(DiceCubeBet bet, CancellationToken ct);
    Task DeleteAsync(long userId, long chatId, CancellationToken ct);
}

public sealed class DiceCubeBetStore(INpgsqlConnectionFactory connections) : IDiceCubeBetStore
{
    public async Task<DiceCubeBet?> FindAsync(long userId, long chatId, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<DiceCubeBet>(new CommandDefinition("""
            SELECT user_id AS UserId, chat_id AS ChatId, amount AS Amount, created_at AS CreatedAt
            FROM dicecube_bets WHERE user_id = @userId AND chat_id = @chatId
            """,
            new { userId, chatId },
            cancellationToken: ct));
    }

    public async Task InsertAsync(DiceCubeBet bet, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition("""
            INSERT INTO dicecube_bets (user_id, chat_id, amount, created_at)
            VALUES (@UserId, @ChatId, @Amount, @CreatedAt)
            """,
            bet,
            cancellationToken: ct));
    }

    public async Task DeleteAsync(long userId, long chatId, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM dicecube_bets WHERE user_id = @userId AND chat_id = @chatId",
            new { userId, chatId },
            cancellationToken: ct));
    }
}

public sealed record DiceCubeBet(long UserId, long ChatId, int Amount, DateTimeOffset CreatedAt);

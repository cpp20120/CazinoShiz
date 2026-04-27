using BotFramework.Sdk;
using Dapper;

namespace BotFramework.Host.Services;

internal sealed class PostgresMiniGameSessionStore(INpgsqlConnectionFactory connections) : IMiniGameSessionStore
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMilliseconds(BotMiniGameSession.TtlMs);

    public async Task<MiniGameSessionBeginResult> TryBeginPlaceBetAsync(
        long userId, long chatId, string gameId, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        await conn.ExecuteAsync(new CommandDefinition(
            """
            DELETE FROM mini_game_sessions
            WHERE user_id = @userId AND chat_id = @chatId AND expires_at <= now()
            """,
            new { userId, chatId },
            transaction: tx,
            cancellationToken: ct));

        var expiresAt = DateTimeOffset.UtcNow.Add(Ttl);
        var inserted = await conn.ExecuteScalarAsync<int?>(new CommandDefinition(
            """
            INSERT INTO mini_game_sessions (user_id, chat_id, game_id, expires_at, updated_at)
            VALUES (@userId, @chatId, @gameId, @expiresAt, now())
            ON CONFLICT (user_id, chat_id) DO NOTHING
            RETURNING 1
            """,
            new { userId, chatId, gameId, expiresAt },
            transaction: tx,
            cancellationToken: ct));

        if (inserted == 1)
        {
            await tx.CommitAsync(ct);
            return new MiniGameSessionBeginResult(true, null);
        }

        var blocker = await conn.ExecuteScalarAsync<string?>(new CommandDefinition(
            """
            SELECT game_id
            FROM mini_game_sessions
            WHERE user_id = @userId AND chat_id = @chatId
            FOR UPDATE
            """,
            new { userId, chatId },
            transaction: tx,
            cancellationToken: ct));

        await tx.CommitAsync(ct);
        return blocker == gameId
            ? new MiniGameSessionBeginResult(true, null)
            : new MiniGameSessionBeginResult(false, blocker);
    }

    public async Task RegisterPlacedBetAsync(long userId, long chatId, string gameId, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        var expiresAt = DateTimeOffset.UtcNow.Add(Ttl);
        await conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO mini_game_sessions (user_id, chat_id, game_id, expires_at, updated_at)
            VALUES (@userId, @chatId, @gameId, @expiresAt, now())
            ON CONFLICT (user_id, chat_id) DO UPDATE SET
                game_id = EXCLUDED.game_id,
                expires_at = EXCLUDED.expires_at,
                updated_at = now()
            """,
            new { userId, chatId, gameId, expiresAt },
            cancellationToken: ct));
    }

    public async Task ClearCompletedRoundAsync(long userId, long chatId, string gameId, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            """
            DELETE FROM mini_game_sessions
            WHERE user_id = @userId AND chat_id = @chatId AND game_id = @gameId
            """,
            new { userId, chatId, gameId },
            cancellationToken: ct));
    }
}

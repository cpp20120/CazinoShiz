using BotFramework.Sdk;
using Dapper;

namespace BotFramework.Host.Services;

internal sealed class PostgresMiniGameRollGateStore(INpgsqlConnectionFactory connections) : IMiniGameRollGateStore
{
    private static readonly TimeSpan Grace = TimeSpan.FromMilliseconds(60_000);

    public async Task ExpectBotRollAsync(string gameId, long userId, long chatId, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        var expiresAt = DateTimeOffset.UtcNow.Add(Grace);
        await conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO mini_game_roll_gates (game_id, user_id, chat_id, expires_at, updated_at)
            VALUES (@gameId, @userId, @chatId, @expiresAt, now())
            ON CONFLICT (game_id, user_id, chat_id) DO UPDATE SET
                expires_at = EXCLUDED.expires_at,
                updated_at = now()
            """,
            new { gameId, userId, chatId, expiresAt },
            cancellationToken: ct));
    }

    public async Task ClearAsync(string gameId, long userId, long chatId, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            """
            DELETE FROM mini_game_roll_gates
            WHERE game_id = @gameId AND user_id = @userId AND chat_id = @chatId
            """,
            new { gameId, userId, chatId },
            cancellationToken: ct));
    }

    public async Task<bool> ShouldIgnoreUserThrowAsync(string gameId, long userId, long chatId, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        var active = await conn.ExecuteScalarAsync<bool>(new CommandDefinition(
            """
            SELECT EXISTS(
                SELECT 1 FROM mini_game_roll_gates
                WHERE game_id = @gameId
                  AND user_id = @userId
                  AND chat_id = @chatId
                  AND expires_at > now()
            )
            """,
            new { gameId, userId, chatId },
            cancellationToken: ct));

        if (!active)
        {
            await conn.ExecuteAsync(new CommandDefinition(
                """
                DELETE FROM mini_game_roll_gates
                WHERE game_id = @gameId AND user_id = @userId AND chat_id = @chatId AND expires_at <= now()
                """,
                new { gameId, userId, chatId },
                cancellationToken: ct));
        }

        return active;
    }
}

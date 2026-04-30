using BotFramework.Host;
using Dapper;

namespace Games.Challenges;

public sealed class ChallengeStore(INpgsqlConnectionFactory connections) : IChallengeStore
{
    public async Task<ChallengeUser?> FindKnownUserByUsernameAsync(long chatId, string username, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<ChallengeUser>(new CommandDefinition("""
            SELECT telegram_user_id AS UserId,
                   display_name AS DisplayName
            FROM users
            WHERE balance_scope_id = @chatId
              AND lower(display_name) = lower(@username)
            ORDER BY updated_at DESC
            LIMIT 1
            """,
            new { chatId, username },
            cancellationToken: ct));
    }

    public async Task<bool> HasPendingAsync(long challengerId, long targetId, long chatId, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<bool>(new CommandDefinition("""
            SELECT EXISTS (
                SELECT 1
                FROM challenge_duels
                WHERE chat_id = @chatId
                  AND status = 'Pending'
                  AND expires_at > now()
                  AND (
                      (challenger_id = @challengerId AND target_id = @targetId)
                      OR (challenger_id = @targetId AND target_id = @challengerId)
                  )
            )
            """,
            new { challengerId, targetId, chatId },
            cancellationToken: ct));
    }

    public async Task InsertAsync(Challenge challenge, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition("""
            INSERT INTO challenge_duels (
                id, chat_id, challenger_id, challenger_name, target_id, target_name,
                amount, game, status, created_at, expires_at
            )
            VALUES (
                @Id, @ChatId, @ChallengerId, @ChallengerName, @TargetId, @TargetName,
                @Amount, @Game, @Status, @CreatedAt, @ExpiresAt
            )
            """,
            ToRow(challenge),
            cancellationToken: ct));
    }

    public async Task<Challenge?> FindAsync(Guid id, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<ChallengeRow>(new CommandDefinition("""
            SELECT id AS Id,
                   chat_id AS ChatId,
                   challenger_id AS ChallengerId,
                   challenger_name AS ChallengerName,
                   target_id AS TargetId,
                   target_name AS TargetName,
                   amount AS Amount,
                   game AS Game,
                   status AS Status,
                   created_at AS CreatedAt,
                   expires_at AS ExpiresAt
            FROM challenge_duels
            WHERE id = @id
            """,
            new { id },
            cancellationToken: ct));
        return row?.ToChallenge();
    }

    public async Task<bool> TryMarkStatusAsync(Guid id, ChallengeStatus expected, ChallengeStatus next, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        var rows = await conn.ExecuteAsync(new CommandDefinition("""
            UPDATE challenge_duels
            SET status = @next,
                responded_at = COALESCE(responded_at, now()),
                completed_at = CASE
                    WHEN @next IN ('Completed', 'Failed', 'Declined') THEN now()
                    ELSE completed_at
                END
            WHERE id = @id
              AND status = @expected
            """,
            new
            {
                id,
                expected = expected.ToString(),
                next = next.ToString(),
            },
            cancellationToken: ct));
        return rows > 0;
    }

    private static object ToRow(Challenge challenge) => new
    {
        challenge.Id,
        challenge.ChatId,
        challenge.ChallengerId,
        challenge.ChallengerName,
        challenge.TargetId,
        challenge.TargetName,
        challenge.Amount,
        Game = challenge.Game.ToString(),
        Status = challenge.Status.ToString(),
        challenge.CreatedAt,
        challenge.ExpiresAt,
    };

    private sealed record ChallengeRow(
        Guid Id,
        long ChatId,
        long ChallengerId,
        string ChallengerName,
        long TargetId,
        string TargetName,
        int Amount,
        string Game,
        string Status,
        DateTimeOffset CreatedAt,
        DateTimeOffset ExpiresAt)
    {
        public Challenge ToChallenge() => new(
            Id,
            ChatId,
            ChallengerId,
            ChallengerName,
            TargetId,
            TargetName,
            Amount,
            Enum.Parse<ChallengeGame>(Game),
            Enum.Parse<ChallengeStatus>(Status),
            CreatedAt,
            ExpiresAt);
    }
}

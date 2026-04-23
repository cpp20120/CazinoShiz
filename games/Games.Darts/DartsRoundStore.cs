using BotFramework.Host;
using Dapper;

namespace Games.Darts;

public sealed class DartsRoundStore(INpgsqlConnectionFactory connections) : IDartsRoundStore
{
    public async Task<long> InsertQueuedAsync(DartsRound row, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        return await conn.QuerySingleAsync<long>(new CommandDefinition("""
            INSERT INTO darts_rounds (user_id, chat_id, amount, created_at, status, bot_message_id, reply_to_message_id)
            VALUES (@UserId, @ChatId, @Amount, @CreatedAt, @Status, @BotMessageId, @ReplyToMessageId)
            RETURNING id
            """,
            new
            {
                row.UserId,
                row.ChatId,
                row.Amount,
                row.CreatedAt,
                Status = (short)row.Status,
                BotMessageId = row.BotMessageId,
                row.ReplyToMessageId,
            },
            cancellationToken: ct));
    }

    public async Task<DartsRound?> FindByIdAsync(long roundId, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<DartsRoundRow>(new CommandDefinition("""
            SELECT id AS Id,
                   user_id AS UserId,
                   chat_id AS ChatId,
                   amount AS Amount,
                   created_at AS CreatedAt,
                   status AS Status,
                   bot_message_id AS BotMessageId,
                   reply_to_message_id AS ReplyToMessageId
            FROM darts_rounds
            WHERE id = @roundId
            """,
            new { roundId },
            cancellationToken: ct));
        return row?.ToRound();
    }

    public async Task<IReadOnlyList<DartsRound>> ListQueuedAsync(CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        var rows = await conn.QueryAsync<DartsRoundRow>(new CommandDefinition("""
            SELECT id AS Id,
                   user_id AS UserId,
                   chat_id AS ChatId,
                   amount AS Amount,
                   created_at AS CreatedAt,
                   status AS Status,
                   bot_message_id AS BotMessageId,
                   reply_to_message_id AS ReplyToMessageId
            FROM darts_rounds
            WHERE status = @queued
            ORDER BY id
            """,
            new { queued = (short)DartsRoundStatus.Queued },
            cancellationToken: ct));
        return rows.Select(r => r.ToRound()).ToArray();
    }

    public async Task<bool> TryMarkAwaitingOutcomeAsync(long roundId, int botMessageId, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        var n = await conn.ExecuteAsync(new CommandDefinition("""
            UPDATE darts_rounds
            SET status = @awaiting, bot_message_id = @botMessageId
            WHERE id = @roundId AND status = @queued
            """,
            new
            {
                roundId,
                botMessageId,
                awaiting = (short)DartsRoundStatus.AwaitingOutcome,
                queued = (short)DartsRoundStatus.Queued,
            },
            cancellationToken: ct));
        return n > 0;
    }

    public async Task DeleteAsync(long roundId, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM darts_rounds WHERE id = @roundId",
            new { roundId },
            cancellationToken: ct));
    }

    public async Task<int> CountRollsAheadInChatAsync(long chatId, long roundId, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition("""
            SELECT COUNT(*)::int FROM darts_rounds
            WHERE chat_id = @chatId AND id < @roundId AND status IN (@q, @a)
            """,
            new
            {
                chatId,
                roundId,
                q = (short)DartsRoundStatus.Queued,
                a = (short)DartsRoundStatus.AwaitingOutcome,
            },
            cancellationToken: ct));
    }

    public async Task<int> CountActiveByUserChatAsync(long userId, long chatId, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition("""
            SELECT COUNT(*)::int FROM darts_rounds
            WHERE user_id = @userId AND chat_id = @chatId AND status IN (@q, @a)
            """,
            new
            {
                userId,
                chatId,
                q = (short)DartsRoundStatus.Queued,
                a = (short)DartsRoundStatus.AwaitingOutcome,
            },
            cancellationToken: ct));
    }

    private sealed class DartsRoundRow
    {
        public long Id { get; init; }
        public long UserId { get; init; }
        public long ChatId { get; init; }
        public int Amount { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        public short Status { get; init; }
        public int? BotMessageId { get; init; }
        public int ReplyToMessageId { get; init; }

        public DartsRound ToRound() => new(
            Id,
            UserId,
            ChatId,
            Amount,
            CreatedAt,
            (DartsRoundStatus)Status,
            BotMessageId,
            ReplyToMessageId);
    }
}

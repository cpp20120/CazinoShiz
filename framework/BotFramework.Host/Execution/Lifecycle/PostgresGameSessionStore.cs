using System.Text.Json;
using BotFramework.Sdk.Execution.Lifecycle;
using Dapper;

namespace BotFramework.Host.Execution.Lifecycle;

/// <summary>PostgreSQL implementation of the durable, transport-neutral session port.</summary>
internal sealed class PostgresGameSessionStore(INpgsqlConnectionFactory connections) : IGameSessionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<GameSession?> GetAsync(string sessionId, CancellationToken ct)
    {
        await using var connection = await connections.OpenAsync(ct);
        return await GetAsync(connection, null, sessionId, ct);
    }

    public async Task<GameSession?> GetByCorrelationAsync(string correlationId, CancellationToken ct)
    {
        await using var connection = await connections.OpenAsync(ct);
        return await GetByCorrelationAsync(connection, null, correlationId, ct);
    }

    private static async Task<GameSession?> GetByCorrelationAsync(
        Npgsql.NpgsqlConnection connection,
        System.Data.IDbTransaction? transaction,
        string correlationId,
        CancellationToken ct)
    {
        var row = await connection.QuerySingleOrDefaultAsync<GameSessionRow>(new CommandDefinition(
            $"""
            {SessionSelect}
            WHERE last_correlation_id = @correlationId
               OR root_correlation_id = @correlationId
               OR session_id = (
                    SELECT session_id
                    FROM game_session_lifecycle
                    WHERE correlation_id = @correlationId)
            """,
            new { correlationId },
            transaction: transaction,
            cancellationToken: ct));
        return row?.ToSession();
    }

    public async Task<GameSession?> GetSnapshotByCorrelationAsync(
        string sessionId,
        string correlationId,
        CancellationToken ct)
    {
        await using var connection = await connections.OpenAsync(ct);
        return await GetSnapshotByCorrelationAsync(connection, null, sessionId, correlationId, ct);
    }

    public async Task<GameSessionWriteResult> TryStartAsync(
        GameSession session,
        GameSessionLifecycleRecord lifecycle,
        CancellationToken ct)
    {
        try
        {
            await using var connection = await connections.OpenAsync(ct);
            await using var transaction = await connection.BeginTransactionAsync(ct);

            var correlated = await GetByCorrelationAsync(connection, transaction, lifecycle.CorrelationId, ct);
            if (correlated is not null)
            {
                await transaction.CommitAsync(ct);
                return new(GameSessionWriteStatus.AlreadyApplied, correlated);
            }

            var applied = await GetSnapshotByCorrelationAsync(connection, transaction, session.SessionId, lifecycle.CorrelationId, ct);
            if (applied is not null)
            {
                await transaction.CommitAsync(ct);
                return new(GameSessionWriteStatus.AlreadyApplied, applied);
            }

            var inserted = await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO game_sessions (
                    session_id, game_id, owner_id, scope_id, root_correlation_id,
                    last_correlation_id, lifecycle, revision, data, failure_code,
                    started_at, updated_at, expires_at)
                VALUES (
                    @SessionId, @GameId, @OwnerId, @ScopeId, @RootCorrelationId,
                    @LastCorrelationId, @Lifecycle, @Revision, CAST(@Data AS jsonb), @FailureCode,
                    @StartedAt, @UpdatedAt, @ExpiresAt)
                ON CONFLICT (session_id) DO NOTHING
                """,
                new
                {
                    session.SessionId,
                    session.GameId,
                    session.OwnerId,
                    session.ScopeId,
                    session.RootCorrelationId,
                    session.LastCorrelationId,
                    Lifecycle = session.Lifecycle.ToString(),
                    session.Revision,
                    session.Data,
                    session.FailureCode,
                    session.StartedAt,
                    session.UpdatedAt,
                    session.ExpiresAt,
                },
                transaction: transaction,
                cancellationToken: ct));
            if (inserted == 0)
            {
                var concurrent = await GetSnapshotByCorrelationAsync(
                    connection, transaction, session.SessionId, lifecycle.CorrelationId, ct);
                if (concurrent is not null)
                {
                    await transaction.CommitAsync(ct);
                    return new(GameSessionWriteStatus.AlreadyApplied, concurrent);
                }

                var existing = await GetAsync(connection, transaction, session.SessionId, ct) ?? session;
                await transaction.CommitAsync(ct);
                return new(GameSessionWriteStatus.SessionAlreadyExists, existing);
            }

            await InsertLifecycleAsync(connection, transaction, session, lifecycle, ct);
            await transaction.CommitAsync(ct);
            return new(GameSessionWriteStatus.Applied, session);
        }
        catch (Npgsql.PostgresException exception)
            when (exception.SqlState == Npgsql.PostgresErrorCodes.UniqueViolation
                  && exception.TableName == "game_session_lifecycle")
        {
            var correlated = await GetByCorrelationAsync(lifecycle.CorrelationId, ct);
            if (correlated is not null)
            {
                return new(GameSessionWriteStatus.AlreadyApplied, correlated);
            }

            throw;
        }
    }

    public async Task<GameSessionWriteResult> TryTransitionAsync(
        GameSession current,
        GameSession updated,
        GameSessionLifecycleRecord lifecycle,
        CancellationToken ct)
    {
        await using var connection = await connections.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        var applied = await GetSnapshotByCorrelationAsync(connection, transaction, updated.SessionId, lifecycle.CorrelationId, ct);
        if (applied is not null)
        {
            await transaction.CommitAsync(ct);
            return new(GameSessionWriteStatus.AlreadyApplied, applied);
        }

        var affected = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE game_sessions
            SET last_correlation_id = @LastCorrelationId,
                lifecycle = @Lifecycle,
                revision = @Revision,
                data = CAST(@Data AS jsonb),
                failure_code = @FailureCode,
                updated_at = @UpdatedAt,
                expires_at = @ExpiresAt
            WHERE session_id = @SessionId AND revision = @ExpectedRevision
            """,
            new
            {
                updated.SessionId,
                updated.LastCorrelationId,
                Lifecycle = updated.Lifecycle.ToString(),
                updated.Revision,
                updated.Data,
                updated.FailureCode,
                updated.UpdatedAt,
                updated.ExpiresAt,
                ExpectedRevision = current.Revision,
            },
            transaction: transaction,
            cancellationToken: ct));
        if (affected == 0)
        {
            var concurrent = await GetSnapshotByCorrelationAsync(
                connection, transaction, updated.SessionId, lifecycle.CorrelationId, ct);
            var latest = concurrent ?? await GetAsync(connection, transaction, updated.SessionId, ct) ?? current;
            await transaction.CommitAsync(ct);
            return new(concurrent is null ? GameSessionWriteStatus.RevisionConflict : GameSessionWriteStatus.AlreadyApplied, latest);
        }

        await InsertLifecycleAsync(connection, transaction, updated, lifecycle, ct);
        await transaction.CommitAsync(ct);
        return new(GameSessionWriteStatus.Applied, updated);
    }

    private static async Task InsertLifecycleAsync(
        Npgsql.NpgsqlConnection connection,
        System.Data.IDbTransaction transaction,
        GameSession session,
        GameSessionLifecycleRecord lifecycle,
        CancellationToken ct) =>
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO game_session_lifecycle (
                session_id, correlation_id, lifecycle, revision, failure_code, snapshot, occurred_at)
            VALUES (
                @SessionId, @CorrelationId, @Lifecycle, @Revision, @FailureCode,
                CAST(@Snapshot AS jsonb), @OccurredAt)
            """,
            new
            {
                lifecycle.SessionId,
                lifecycle.CorrelationId,
                Lifecycle = lifecycle.Lifecycle.ToString(),
                lifecycle.Revision,
                lifecycle.FailureCode,
                Snapshot = JsonSerializer.Serialize(session, JsonOptions),
                lifecycle.OccurredAt,
            },
            transaction: transaction,
            cancellationToken: ct));

    private static async Task<GameSession?> GetSnapshotByCorrelationAsync(
        Npgsql.NpgsqlConnection connection,
        System.Data.IDbTransaction? transaction,
        string sessionId,
        string correlationId,
        CancellationToken ct)
    {
        var snapshot = await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            """
            SELECT snapshot::text
            FROM game_session_lifecycle
            WHERE session_id = @sessionId AND correlation_id = @correlationId
            """,
            new { sessionId, correlationId },
            transaction: transaction,
            cancellationToken: ct));
        return snapshot is null ? null : JsonSerializer.Deserialize<GameSession>(snapshot, JsonOptions);
    }

    private static async Task<GameSession?> GetAsync(
        Npgsql.NpgsqlConnection connection,
        System.Data.IDbTransaction? transaction,
        string sessionId,
        CancellationToken ct)
    {
        var row = await connection.QuerySingleOrDefaultAsync<GameSessionRow>(new CommandDefinition(
            $"""
            {SessionSelect}
            WHERE session_id = @sessionId
            """,
            new { sessionId },
            transaction: transaction,
            cancellationToken: ct));
        return row?.ToSession();
    }

    private const string SessionSelect = """
        SELECT session_id AS SessionId,
               game_id AS GameId,
               owner_id AS OwnerId,
               scope_id AS ScopeId,
               root_correlation_id AS RootCorrelationId,
               last_correlation_id AS LastCorrelationId,
               lifecycle AS Lifecycle,
               revision AS Revision,
               data::text AS Data,
               failure_code AS FailureCode,
               started_at AS StartedAt,
               updated_at AS UpdatedAt,
               expires_at AS ExpiresAt
        FROM game_sessions
        """;

    private sealed class GameSessionRow
    {
        public required string SessionId { get; init; }
        public required string GameId { get; init; }
        public required string OwnerId { get; init; }
        public required string ScopeId { get; init; }
        public required string RootCorrelationId { get; init; }
        public required string LastCorrelationId { get; init; }
        public required string Lifecycle { get; init; }
        public long Revision { get; init; }
        public required string Data { get; init; }
        public string? FailureCode { get; init; }
        public DateTimeOffset StartedAt { get; init; }
        public DateTimeOffset UpdatedAt { get; init; }
        public DateTimeOffset? ExpiresAt { get; init; }

        public GameSession ToSession() => new(
            SessionId,
            GameId,
            OwnerId,
            ScopeId,
            RootCorrelationId,
            LastCorrelationId,
            Enum.Parse<GameSessionLifecycle>(Lifecycle, ignoreCase: false),
            Revision,
            Data,
            FailureCode,
            StartedAt,
            UpdatedAt,
            ExpiresAt);
    }
}

using Dapper;

namespace BotFramework.Host;

public interface IEventDispatchFailureStore
{
    Task RecordAsync(EventDispatchFailure failure, CancellationToken ct);
    Task<IReadOnlyList<EventDispatchFailureRow>> ListUnresolvedAsync(int limit, CancellationToken ct);
    Task<EventDispatchFailureRow?> FindAsync(long id, CancellationToken ct);
    Task MarkResolvedAsync(long id, CancellationToken ct);
}

public sealed record EventDispatchFailure(
    string StreamId,
    long StreamVersion,
    string EventType,
    string Stage,
    string HandlerName,
    string Error,
    string? ErrorType);

public sealed record EventDispatchFailureRow(
    long Id,
    string StreamId,
    long StreamVersion,
    string EventType,
    string Stage,
    string HandlerName,
    string Error,
    string? ErrorType,
    int RetryCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastSeenAt);

public sealed class PostgresEventDispatchFailureStore(
    INpgsqlConnectionFactory connections) : IEventDispatchFailureStore
{
    public async Task RecordAsync(EventDispatchFailure failure, CancellationToken ct)
    {
        const string sql = """
            INSERT INTO event_dispatch_failures (
                stream_id,
                stream_version,
                event_type,
                stage,
                handler_name,
                error,
                error_type,
                retry_count,
                created_at,
                last_seen_at
            )
            VALUES (
                @streamId,
                @streamVersion,
                @eventType,
                @stage,
                @handlerName,
                @error,
                @errorType,
                0,
                now(),
                now()
            )
            ON CONFLICT (stream_id, stream_version, stage, handler_name) DO UPDATE SET
                error = EXCLUDED.error,
                error_type = EXCLUDED.error_type,
                retry_count = event_dispatch_failures.retry_count + 1,
                last_seen_at = now(),
                resolved_at = NULL
            """;

        await using var conn = await connections.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(sql, new
        {
            streamId = failure.StreamId,
            streamVersion = failure.StreamVersion,
            eventType = failure.EventType,
            stage = failure.Stage,
            handlerName = failure.HandlerName,
            error = Truncate(failure.Error, 8_000),
            errorType = failure.ErrorType,
        }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<EventDispatchFailureRow>> ListUnresolvedAsync(int limit, CancellationToken ct)
    {
        const string sql = """
            SELECT id,
                   stream_id AS StreamId,
                   stream_version AS StreamVersion,
                   event_type AS EventType,
                   stage AS Stage,
                   handler_name AS HandlerName,
                   error AS Error,
                   error_type AS ErrorType,
                   retry_count AS RetryCount,
                   created_at AS CreatedAt,
                   last_seen_at AS LastSeenAt
            FROM event_dispatch_failures
            WHERE resolved_at IS NULL
            ORDER BY last_seen_at DESC, id DESC
            LIMIT @limit
            """;

        await using var conn = await connections.OpenAsync(ct);
        var rows = await conn.QueryAsync<EventDispatchFailureRow>(new CommandDefinition(
            sql,
            new { limit = Math.Clamp(limit, 1, 100) },
            cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<EventDispatchFailureRow?> FindAsync(long id, CancellationToken ct)
    {
        const string sql = """
            SELECT id,
                   stream_id AS StreamId,
                   stream_version AS StreamVersion,
                   event_type AS EventType,
                   stage AS Stage,
                   handler_name AS HandlerName,
                   error AS Error,
                   error_type AS ErrorType,
                   retry_count AS RetryCount,
                   created_at AS CreatedAt,
                   last_seen_at AS LastSeenAt
            FROM event_dispatch_failures
            WHERE id = @id AND resolved_at IS NULL
            """;

        await using var conn = await connections.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<EventDispatchFailureRow>(new CommandDefinition(
            sql,
            new { id },
            cancellationToken: ct));
    }

    public async Task MarkResolvedAsync(long id, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE event_dispatch_failures SET resolved_at = now() WHERE id = @id",
            new { id },
            cancellationToken: ct));
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
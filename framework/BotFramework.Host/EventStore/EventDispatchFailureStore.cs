using Dapper;

namespace BotFramework.Host;

public interface IEventDispatchFailureStore
{
    Task RecordAsync(EventDispatchFailure failure, CancellationToken ct);
}

public sealed record EventDispatchFailure(
    string StreamId,
    long StreamVersion,
    string EventType,
    string Stage,
    string HandlerName,
    string Error,
    string? ErrorType);

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

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}

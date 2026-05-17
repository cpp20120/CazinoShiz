using BotFramework.Sdk;
using Dapper;

namespace BotFramework.Host;

public interface IEventDispatchRetryService
{
    Task<EventDispatchRetryResult> RetryAsync(long failureId, CancellationToken ct);
}

public sealed record EventDispatchRetryResult(
    bool Success,
    bool NotFound,
    string? Message = null);

public sealed class EventDispatchRetryService(
    INpgsqlConnectionFactory connections,
    IEventDispatchFailureStore failures,
    IEventSerializer serializer,
    EventDispatcher dispatcher) : IEventDispatchRetryService
{
    public async Task<EventDispatchRetryResult> RetryAsync(long failureId, CancellationToken ct)
    {
        var failure = await failures.FindAsync(failureId, ct);
        if (failure is null)
            return new EventDispatchRetryResult(false, true, "failure not found or already resolved");

        var stored = await LoadEventAsync(failure.StreamId, failure.StreamVersion, ct);
        if (stored is null)
            return new EventDispatchRetryResult(false, true, "source event not found");

        var ev = serializer.Deserialize(stored.EventType, stored.PayloadText);
        if (ev is null)
            return new EventDispatchRetryResult(false, false, $"cannot deserialize event {stored.EventType}");

        await dispatcher.DispatchAsync(failure.StreamId, failure.StreamVersion, ev, transaction: null, ct);
        await failures.MarkResolvedAsync(failureId, ct);
        return new EventDispatchRetryResult(true, false, "retry succeeded");
    }

    private async Task<EventRow?> LoadEventAsync(string streamId, long streamVersion, CancellationToken ct)
    {
        const string sql = """
            SELECT stream_id AS StreamId,
                   version AS Version,
                   event_type AS EventType,
                   payload::text AS PayloadText
            FROM module_events
            WHERE stream_id = @streamId AND version = @streamVersion
            """;

        await using var conn = await connections.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<EventRow>(new CommandDefinition(
            sql,
            new { streamId, streamVersion },
            cancellationToken: ct));
    }

    private sealed record EventRow(
        string StreamId,
        long Version,
        string EventType,
        string PayloadText);
}

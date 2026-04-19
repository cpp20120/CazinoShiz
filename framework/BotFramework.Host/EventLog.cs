using BotFramework.Sdk;
using Dapper;

namespace BotFramework.Host;

public interface IEventLog
{
    Task AppendAsync(IDomainEvent ev, CancellationToken ct);
}

public sealed partial class PostgresEventLog(
    INpgsqlConnectionFactory connections,
    IEventSerializer serializer,
    ILogger<PostgresEventLog> logger) : IEventLog
{
    public async Task AppendAsync(IDomainEvent ev, CancellationToken ct)
    {
        try
        {
            await using var conn = await connections.OpenAsync(ct);
            await conn.ExecuteAsync(new CommandDefinition("""
                INSERT INTO event_log (event_type, payload, occurred_at)
                VALUES (@type, @payload::jsonb, to_timestamp(@ms / 1000.0))
                """,
                new
                {
                    type = ev.EventType,
                    payload = serializer.Serialize(ev),
                    ms = ev.OccurredAt,
                },
                cancellationToken: ct));
        }
        catch (Exception ex)
        {
            LogAppendFailed(ev.EventType, ex);
        }
    }

    [LoggerMessage(EventId = 1700, Level = LogLevel.Warning,
        Message = "event_log.append.failed event_type={EventType}")]
    partial void LogAppendFailed(string eventType, Exception exception);
}

internal sealed class EventLogSubscriber(IEventLog log) : IDomainEventSubscriber
{
    public Task HandleAsync(IDomainEvent ev, CancellationToken ct) => log.AppendAsync(ev, ct);
}

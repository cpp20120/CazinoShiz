// ─────────────────────────────────────────────────────────────────────────────
// PostgresEventStore — concrete IEventStore for event-sourced modules.
//
// One table (module_events) serves every module. Event types are namespaced
// ("sh.game_created", "poker.hand_dealt"), so they co-exist fine in one stream
// store. A unique constraint on (stream_id, version) gives us optimistic
// concurrency for free — two writers racing on the same stream will have one
// fail with a unique-violation (Postgres 23505), which we translate to
// ConcurrencyException.
//
// Schema is applied by the Host's own IModuleMigrations entry
// (FrameworkMigrations.cs) before any module migration runs.
//
// Why one shared table and not per-module:
//   • replay for a single stream is a single-index lookup regardless of which
//     module owns it
//   • analytics queries ("count of sh.election_failed this week") stay uniform
//   • per-module tables would force the Host to know every module's schema
//     at migration time — exactly the coupling we're trying to avoid
// ─────────────────────────────────────────────────────────────────────────────

using BotFramework.Sdk;
using Dapper;
using Npgsql;

namespace BotFramework.Host;

public sealed class PostgresEventStore(
    INpgsqlConnectionFactory connections,
    IEventSerializer serializer) : IEventStore
{
    public async Task<IReadOnlyList<StoredEvent>> LoadAsync(string streamId, CancellationToken ct)
    {
        const string sql = """
            SELECT stream_id, version, event_type, payload::text AS payload_text,
                   (extract(epoch from occurred_at) * 1000)::bigint AS occurred_at_ms
            FROM module_events
            WHERE stream_id = @streamId
            ORDER BY version ASC
            """;

        await using var conn = await connections.OpenAsync(ct);
        var rows = await conn.QueryAsync<EventRow>(new CommandDefinition(sql, new { streamId }, cancellationToken: ct));

        return rows.Select(r => new StoredEvent(
            StreamId: r.stream_id,
            Version: r.version,
            EventType: r.event_type,
            PayloadJson: r.payload_text,
            OccurredAt: r.occurred_at_ms)).ToList();
    }

    public async Task AppendAsync(
        string streamId,
        long expectedVersion,
        IEnumerable<IDomainEvent> events,
        CancellationToken ct)
    {
        const string sql = """
            INSERT INTO module_events (stream_id, version, event_type, payload, occurred_at)
            VALUES (@streamId, @version, @eventType, @payload::jsonb, to_timestamp(@occurredAt / 1000.0))
            """;

        await using var conn = await connections.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var version = expectedVersion;
        try
        {
            foreach (var ev in events)
            {
                version++;
                await conn.ExecuteAsync(new CommandDefinition(sql, new
                {
                    streamId,
                    version,
                    eventType = ev.EventType,
                    payload = serializer.Serialize(ev),
                    occurredAt = ev.OccurredAt,
                }, transaction: tx, cancellationToken: ct));
            }
            await tx.CommitAsync(ct);
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            await tx.RollbackAsync(ct);
            throw new ConcurrencyException(streamId, expectedVersion, version);
        }
    }

    // Dapper's default column binding is case-insensitive snake-case-friendly;
    // lowercase names here match the returned column identifiers exactly.
    private sealed record EventRow(string stream_id, long version, string event_type, string payload_text, long occurred_at_ms);
}

public interface IEventSerializer
{
    string Serialize(IDomainEvent ev);
    IDomainEvent? Deserialize(string eventType, string payloadJson);
}

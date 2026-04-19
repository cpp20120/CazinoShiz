// ─────────────────────────────────────────────────────────────────────────────
// PostgresSnapshotStore — concrete ISnapshotStore for every ES aggregate type.
//
// One table, module_snapshots, serves every module:
//
//   CREATE TABLE module_snapshots (
//       stream_id     TEXT         PRIMARY KEY,
//       aggregate     TEXT         NOT NULL,
//       version       BIGINT       NOT NULL,
//       state         JSONB        NOT NULL,
//       taken_at      TIMESTAMPTZ  NOT NULL DEFAULT now()
//   );
//
// Why one row per stream (instead of append-only snapshot history):
//   snapshots are a cache. If operators want "state at event N" they replay
//   the stream up to N — that's what event sourcing is for. Keeping only the
//   newest snapshot halves storage cost and makes the UPSERT trivially safe.
// ─────────────────────────────────────────────────────────────────────────────

using System.Text.Json;
using BotFramework.Sdk;
using Dapper;

namespace BotFramework.Host;

public sealed class PostgresSnapshotStore<TAggregate>(
    INpgsqlConnectionFactory connections) : ISnapshotStore<TAggregate>
    where TAggregate : class, IAggregateRoot
{
    private static readonly string AggregateName = typeof(TAggregate).Name;

    public async Task<StoredSnapshot?> LoadLatestAsync(string streamId, CancellationToken ct)
    {
        const string sql = """
            SELECT version, state::text AS state_json,
                   (extract(epoch from taken_at) * 1000)::bigint AS taken_at_ms
            FROM module_snapshots
            WHERE stream_id = @streamId AND aggregate = @aggregate
            """;

        await using var conn = await connections.OpenAsync(ct);
        var row = await conn.QueryFirstOrDefaultAsync<SnapshotRow>(
            new CommandDefinition(sql, new { streamId, aggregate = AggregateName }, cancellationToken: ct));

        return row is null ? null : new StoredSnapshot(row.version, row.state_json, row.taken_at_ms);
    }

    public async Task SaveAsync(string streamId, long version, object state, CancellationToken ct)
    {
        const string sql = """
            INSERT INTO module_snapshots (stream_id, aggregate, version, state)
            VALUES (@streamId, @aggregate, @version, @state::jsonb)
            ON CONFLICT (stream_id)
            DO UPDATE SET version = EXCLUDED.version,
                          state = EXCLUDED.state,
                          taken_at = now()
            """;

        await using var conn = await connections.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(sql, new
        {
            streamId,
            aggregate = AggregateName,
            version,
            state = JsonSerializer.Serialize(state),
        }, cancellationToken: ct));
    }

    private sealed record SnapshotRow(long version, string state_json, long taken_at_ms);
}

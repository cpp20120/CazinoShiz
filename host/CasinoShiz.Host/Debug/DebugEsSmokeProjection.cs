using BotFramework.Host;
using BotFramework.Sdk;
using Dapper;

namespace CasinoShiz.Host.Debug;

public sealed class DebugEsSmokeProjection(
    INpgsqlConnectionFactory connections) : IRebuildableProjection
{
    public IReadOnlySet<string> SubscribedEventTypes { get; } =
        new HashSet<string> { "debug.es_smoke_incremented" };

    public async Task ApplyAsync(IDomainEvent ev, ProjectionContext ctx, CancellationToken ct)
    {
        if (ev is not DebugEsSmokeIncremented incremented)
            return;

        const string sql = """
            INSERT INTO debug_es_smoke_projection (stream_id, count, stream_version, updated_at_ms)
            VALUES (@streamId, @count, @streamVersion, @updatedAtMs)
            ON CONFLICT (stream_id) DO UPDATE SET
                count = EXCLUDED.count,
                stream_version = EXCLUDED.stream_version,
                updated_at_ms = EXCLUDED.updated_at_ms
            WHERE debug_es_smoke_projection.stream_version <= EXCLUDED.stream_version
            """;

        await using var conn = await connections.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(sql, new
        {
            streamId = ctx.StreamId,
            count = incremented.Count,
            streamVersion = ctx.StreamVersion,
            updatedAtMs = ctx.OccurredAt,
        }, cancellationToken: ct));
    }

    public async Task ResetAsync(CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            "TRUNCATE TABLE debug_es_smoke_projection",
            cancellationToken: ct));
    }
}
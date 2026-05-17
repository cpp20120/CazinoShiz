using BotFramework.Sdk;
using Dapper;

namespace BotFramework.Host;

public interface IEventReplayService
{
    IReadOnlyList<ProjectionDescriptor> ListRebuildableProjections();

    Task<ProjectionReplayResult> RebuildProjectionAsync(
        string projectionName,
        CancellationToken ct);
}

public sealed record ProjectionDescriptor(
    string Name,
    string FullName,
    IReadOnlySet<string> SubscribedEventTypes);

public sealed record ProjectionReplayResult(
    string ProjectionName,
    long EventsSeen,
    long EventsApplied);

public sealed class EventReplayService(
    INpgsqlConnectionFactory connections,
    IEventSerializer serializer,
    IEnumerable<IRebuildableProjection> rebuildableProjections) : IEventReplayService
{
    private readonly IReadOnlyList<IRebuildableProjection> _projections = rebuildableProjections.ToList();

    public IReadOnlyList<ProjectionDescriptor> ListRebuildableProjections() =>
        _projections
            .Select(p => new ProjectionDescriptor(
                p.GetType().Name,
                p.GetType().FullName ?? p.GetType().Name,
                p.SubscribedEventTypes))
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public async Task<ProjectionReplayResult> RebuildProjectionAsync(
        string projectionName,
        CancellationToken ct)
    {
        var projection = ResolveProjection(projectionName);
        await projection.ResetAsync(ct);

        var subscribedTypes = projection.SubscribedEventTypes.ToArray();
        if (subscribedTypes.Length == 0)
            return new ProjectionReplayResult(projection.GetType().Name, 0, 0);

        const string sql = """
            SELECT stream_id, version, event_type, payload::text AS payload_text,
                   (extract(epoch from occurred_at) * 1000)::bigint AS occurred_at_ms
            FROM module_events
            WHERE event_type = ANY(@eventTypes)
            ORDER BY occurred_at ASC, stream_id ASC, version ASC
            """;

        await using var conn = await connections.OpenAsync(ct);
        var rows = await conn.QueryAsync<EventRow>(new CommandDefinition(
            sql,
            new { eventTypes = subscribedTypes },
            cancellationToken: ct));

        long seen = 0;
        long applied = 0;
        foreach (var row in rows)
        {
            seen++;
            var ev = serializer.Deserialize(row.event_type, row.payload_text);
            if (ev is null)
                throw new InvalidOperationException(
                    $"Cannot deserialize event '{row.event_type}' while replaying projection '{projection.GetType().Name}'.");

            var ctx = new ProjectionContext(row.stream_id, row.version, row.occurred_at_ms, Transaction: null);
            await projection.ApplyAsync(ev, ctx, ct);
            applied++;
        }

        return new ProjectionReplayResult(projection.GetType().Name, seen, applied);
    }

    private IRebuildableProjection ResolveProjection(string projectionName)
    {
        foreach (var projection in _projections)
        {
            var type = projection.GetType();
            if (string.Equals(type.Name, projectionName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(type.FullName, projectionName, StringComparison.OrdinalIgnoreCase))
            {
                return projection;
            }
        }

        var available = string.Join(", ", _projections.Select(p => p.GetType().Name).Order(StringComparer.OrdinalIgnoreCase));
        throw new InvalidOperationException(
            available.Length == 0
                ? "No rebuildable projections are registered."
                : $"Unknown projection '{projectionName}'. Available: {available}");
    }

    private sealed record EventRow(
        string stream_id,
        long version,
        string event_type,
        string payload_text,
        long occurred_at_ms);
}

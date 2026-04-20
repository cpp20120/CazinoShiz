// ─────────────────────────────────────────────────────────────────────────────
// EventDispatcher — the glue between the event store and the read-model
// projections.
//
// When EventStoreRepository.SaveAsync calls IEventStore.AppendAsync, the Host
// implementation of the store wraps the append in a transaction and, after
// the INSERTs, asks this dispatcher to fan the same events out to every
// projection that subscribes. The projection's update runs in the SAME
// transaction — so projection state and event state commit or roll back
// together.
//
// Why not a separate async worker draining a queue:
//   • simpler: no at-least-once dedupe, no projection-lag monitoring dashboards
//   • consistent: admin pages never show "this game exists but the event that
//     created it hasn't been projected yet" lag
//   • bounded cost: projections here do minimal work (one upsert per event);
//     if a projection grows expensive, *that* specific projection graduates
//     to async with a cursor, not the whole system
//
// Also: analytics emission (IAnalyticsService.Track) hangs off the same
// dispatch hook. Every domain event becomes an analytics event for free —
// the module doesn't write Track() boilerplate in handlers.
// ─────────────────────────────────────────────────────────────────────────────

using BotFramework.Sdk;

namespace BotFramework.Host;

public sealed class EventDispatcher(
    IEnumerable<IProjection> projections,
    IDomainEventBus bus,
    IAnalyticsService analytics)
{
    private readonly Dictionary<string, List<IProjection>> _byEventType = BuildIndex(projections);

    public async Task DispatchAsync(
        string streamId,
        long streamVersion,
        IDomainEvent ev,
        object transaction,
        CancellationToken ct)
    {
        // 1. Projections first — they maintain read models other subscribers
        //    may want to query. Same transaction as the event-store append.
        if (_byEventType.TryGetValue(ev.EventType, out var subscribers))
        {
            var ctx = new ProjectionContext(streamId, streamVersion, ev.OccurredAt, transaction);
            foreach (var proj in subscribers)
                await proj.ApplyAsync(ev, ctx, ct);
        }

        // 2. Cross-module subscribers — anyone who registered an IDomainEvent
        //    subscriber for this event type (or a wildcard pattern matching it).
        //    Also same transaction; slow subscribers trigger their own jobs.
        await bus.PublishAsync(ev, ct);

        // 3. Analytics. Every domain event becomes an analytics event; module
        //    prefix ("sh.*", "poker.*") is enough to bucket.
        analytics.Track(
            moduleId: ev.EventType.Split('.', 2)[0],
            eventName: ev.EventType,
            tags: new Dictionary<string, object?>
            {
                ["stream_id"] = streamId,
                ["stream_version"] = streamVersion,
                ["occurred_at"] = ev.OccurredAt,
            });
    }

    private static Dictionary<string, List<IProjection>> BuildIndex(IEnumerable<IProjection> projections)
    {
        var index = new Dictionary<string, List<IProjection>>();
        foreach (var proj in projections)
        foreach (var type in proj.SubscribedEventTypes)
        {
            if (!index.TryGetValue(type, out var list))
                index[type] = list = [];
            list.Add(proj);
        }
        return index;
    }
}

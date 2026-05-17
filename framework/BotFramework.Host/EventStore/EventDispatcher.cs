// ─────────────────────────────────────────────────────────────────────────────
// EventDispatcher — fans persisted aggregate events into read-model projections,
// cross-module subscribers and framework analytics.
//
// IEventStore only stores events. EventSourcedRepository<T> owns the aggregate
// lifecycle and invokes this dispatcher after a successful append. That means
// every event-sourced aggregate registered with
// RegisterAggregate<T>(PersistenceStrategy.EventSourced) automatically flows
// through projections, EventLogSubscriber, ClickHouseEventMirror, module
// subscriptions and analytics.
//
// Current delivery semantics are post-commit: if dispatch fails, the original
// event append stays committed and the failure is logged by the repository.
// Projection rebuild/replay is the recovery mechanism. A future same-transaction
// unit-of-work can pass a provider-specific transaction object through
// ProjectionContext.Transaction without changing module contracts.
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
        object? transaction,
        CancellationToken ct)
    {
        // 1. Projections first — they maintain read models other subscribers
        //    may want to query. Transaction is null in the current post-commit
        //    mode and may be provider-specific in a future unit-of-work mode.
        if (_byEventType.TryGetValue(ev.EventType, out var subscribers))
        {
            var ctx = new ProjectionContext(streamId, streamVersion, ev.OccurredAt, transaction);
            foreach (var proj in subscribers)
                await proj.ApplyAsync(ev, ctx, ct);
        }

        // 2. Cross-module subscribers — anyone who registered an IDomainEvent
        //    subscriber for this event type (or a wildcard pattern matching it).
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
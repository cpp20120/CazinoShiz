// ─────────────────────────────────────────────────────────────────────────────
// EventSourcedRepository — IRepository<T> implementation for event-sourced
// aggregates.
//
// IEventStore deliberately stays a small load/append persistence primitive.
// This repository owns the aggregate lifecycle around it:
//   • load stored events and replay them through the aggregate
//   • append PendingEvents with optimistic concurrency
//   • dispatch appended events to projections, cross-module subscribers and
//     framework-wide event mirrors
//   • mark pending events as committed after the append succeeds
//
// Dispatch currently runs after the event-store commit. This keeps the append
// path robust and lets failed projections/mirrors be rebuilt or retried later
// without risking a duplicate append from the original command.
// ─────────────────────────────────────────────────────────────────────────────

using BotFramework.Sdk;
using Microsoft.Extensions.DependencyInjection;

namespace BotFramework.Host;

public sealed class DefaultAggregateFactory<TAggregate>(IServiceProvider services) : IAggregateFactory<TAggregate>
    where TAggregate : class, IAggregateRoot
{
    public TAggregate Create(string id)
    {
        try
        {
            return ActivatorUtilities.CreateInstance<TAggregate>(services, id);
        }
        catch (InvalidOperationException)
        {
            try
            {
                return ActivatorUtilities.CreateInstance<TAggregate>(services);
            }
            catch (InvalidOperationException ex)
            {
                throw new InvalidOperationException(
                    $"Cannot create aggregate {typeof(TAggregate).FullName}. " +
                    "Register a custom IAggregateFactory<TAggregate>, or expose a public constructor " +
                    "that can be satisfied by DI, optionally with the stream id as a string argument.",
                    ex);
            }
        }
    }
}

public sealed partial class EventSourcedRepository<TAggregate>(
    IEventStore eventStore,
    IEventSerializer serializer,
    IAggregateFactory<TAggregate> aggregateFactory,
    EventDispatcher dispatcher,
    ILogger<EventSourcedRepository<TAggregate>> logger) : IRepository<TAggregate>
    where TAggregate : class, IEventSourcedAggregate
{
    public async Task<TAggregate?> FindAsync(string id, CancellationToken ct)
    {
        var stored = await eventStore.LoadAsync(id, ct);
        if (stored.Count == 0)
            return null;

        var events = new List<IDomainEvent>(stored.Count);
        foreach (var row in stored)
        {
            var ev = serializer.Deserialize(row.EventType, row.PayloadJson);
            if (ev is null)
                throw new InvalidOperationException(
                    $"Cannot deserialize event '{row.EventType}' while loading stream '{id}'.");

            events.Add(ev);
        }

        var aggregate = aggregateFactory.Create(id);
        aggregate.LoadFromHistory(events);
        aggregate.MarkEventsCommitted();
        return aggregate;
    }

    public async Task SaveAsync(TAggregate aggregate, CancellationToken ct)
    {
        var pending = aggregate.PendingEvents.ToArray();
        if (pending.Length == 0)
            return;

        var expectedVersion = aggregate.Version - pending.Length;
        if (expectedVersion < 0)
            throw new InvalidOperationException(
                $"Aggregate {typeof(TAggregate).FullName} has version {aggregate.Version} " +
                $"but {pending.Length} pending events. Version must include already-applied pending events.");

        await eventStore.AppendAsync(aggregate.Id, expectedVersion, pending, ct);

        var streamVersion = expectedVersion;
        foreach (var ev in pending)
        {
            streamVersion++;
            try
            {
                await dispatcher.DispatchAsync(aggregate.Id, streamVersion, ev, transaction: null, ct);
            }
            catch (Exception ex)
            {
                LogDispatchFailed(ex, aggregate.Id, streamVersion, ev.EventType);
            }
        }

        aggregate.MarkEventsCommitted();
    }

    [LoggerMessage(EventId = 1800, Level = LogLevel.Warning,
        Message = "event_sourced_repository.dispatch_failed stream_id={StreamId} stream_version={StreamVersion} event_type={EventType}")]
    partial void LogDispatchFailed(Exception exception, string streamId, long streamVersion, string eventType);
}
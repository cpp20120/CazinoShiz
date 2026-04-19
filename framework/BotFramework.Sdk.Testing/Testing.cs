// ─────────────────────────────────────────────────────────────────────────────
// Testing helpers for module authors.
//
// The contract is small: IRepository<T> / IEventStore / IEconomicsService /
// IAnalyticsService — so the framework can provide in-memory stubs that let
// module authors write xUnit tests against their application services with
// zero external infrastructure. Zero Postgres, zero Telegram, zero network.
//
// This file would ship as a separate BotFramework.Sdk.Testing NuGet package so
// InMemoryRepository<T> never sneaks into production references.
//
// Design note: the stubs are deliberately dumb. They don't simulate network
// failures or concurrency races. For those you write integration tests that
// spin up Postgres via Testcontainers; no framework feature for that here.
// ─────────────────────────────────────────────────────────────────────────────

namespace BotFramework.Sdk.Testing;

/// Classical-aggregate repository backed by a dictionary. Stable semantics:
/// FindAsync returns the live reference (mutations in test code are visible
/// on subsequent Find calls), SaveAsync is a no-op replace. That matches EF
/// identity-map behavior closely enough for service-level tests.
public sealed class InMemoryRepository<TAggregate> : IRepository<TAggregate>
    where TAggregate : class, IAggregateRoot
{
    private readonly Dictionary<string, TAggregate> _store = new();

    public Task<TAggregate?> FindAsync(string id, CancellationToken ct) =>
        Task.FromResult(_store.GetValueOrDefault(id));

    public Task SaveAsync(TAggregate aggregate, CancellationToken ct)
    {
        _store[aggregate.Id] = aggregate;
        return Task.CompletedTask;
    }

    public IReadOnlyDictionary<string, TAggregate> Snapshot => _store;
}

/// Event-store-aggregate repository backed by in-memory streams. Preserves
/// optimistic-concurrency semantics — if SaveAsync is called twice with the
/// same expectedVersion, the second throws, same as production.
public sealed class InMemoryEventStoreRepository<TAggregate> : IRepository<TAggregate>
    where TAggregate : class, IEventSourcedAggregate, new()
{
    private readonly Dictionary<string, List<IDomainEvent>> _streams = new();

    public Task<TAggregate?> FindAsync(string id, CancellationToken ct)
    {
        if (!_streams.TryGetValue(id, out var stream) || stream.Count == 0)
            return Task.FromResult<TAggregate?>(null);

        var agg = new TAggregate();
        agg.LoadFromHistory(stream);
        return Task.FromResult<TAggregate?>(agg);
    }

    public Task SaveAsync(TAggregate aggregate, CancellationToken ct)
    {
        if (aggregate.PendingEvents.Count == 0) return Task.CompletedTask;

        if (!_streams.TryGetValue(aggregate.Id, out var stream))
            _streams[aggregate.Id] = stream = [];

        var expectedVersion = aggregate.Version - aggregate.PendingEvents.Count;
        if (stream.Count != expectedVersion)
            throw new InvalidOperationException(
                $"concurrency: stream at {stream.Count}, expected {expectedVersion}");

        stream.AddRange(aggregate.PendingEvents);
        aggregate.MarkEventsCommitted();
        return Task.CompletedTask;
    }

    public IReadOnlyList<IDomainEvent> StreamFor(string id) =>
        _streams.GetValueOrDefault(id) ?? (IReadOnlyList<IDomainEvent>)Array.Empty<IDomainEvent>();
}

/// Tracks debits/credits so tests can assert the balance ledger is right
/// without a real economics service. Starts every user at 1_000 by default.
public sealed class FakeEconomicsService
{
    private readonly Dictionary<long, long> _balances = new();
    private readonly long _startingBalance;
    public List<(long UserId, int Amount, string Reason)> Debits { get; } = [];
    public List<(long UserId, int Amount, string Reason)> Credits { get; } = [];

    public FakeEconomicsService(long startingBalance = 1_000) => _startingBalance = startingBalance;

    public Task<long> GetBalanceAsync(long userId, CancellationToken ct) =>
        Task.FromResult(_balances.GetValueOrDefault(userId, _startingBalance));

    public Task DebitAsync(long userId, int amount, string reason, CancellationToken ct)
    {
        _balances[userId] = _balances.GetValueOrDefault(userId, _startingBalance) - amount;
        Debits.Add((userId, amount, reason));
        return Task.CompletedTask;
    }

    public Task CreditAsync(long userId, int amount, string reason, CancellationToken ct)
    {
        _balances[userId] = _balances.GetValueOrDefault(userId, _startingBalance) + amount;
        Credits.Add((userId, amount, reason));
        return Task.CompletedTask;
    }
}

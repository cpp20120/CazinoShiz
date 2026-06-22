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

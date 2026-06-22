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

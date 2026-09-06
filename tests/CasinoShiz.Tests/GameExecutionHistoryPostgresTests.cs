using BotFramework.Host.Execution;
using BotFramework.Host.Persistence.Connections;
using BotFramework.Sdk.Execution;
using Npgsql;
using System.Text.Json;
using Xunit;

namespace CasinoShiz.Tests;

[Collection(AtomicPostgresCollection.Name)]
public sealed class GameExecutionHistoryPostgresTests(AtomicPostgresFixture database) : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 2, 12, 0, 0, TimeSpan.Zero);

    public Task InitializeAsync() => database.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task CommittedDecision_CapturesInputSnapshotsEntropyAndEveryEffectCategory()
    {
        var factory = new PostgresGameExecutionSessionFactory(new TestConnectionFactory(database.ConnectionString));
        await using var session = await factory.BeginAsync(CancellationToken.None);
        var decision = new GameDecision<HistoryState, HistoryResult>(
            DecisionStatus.Accepted,
            new HistoryState(2),
            new HistoryResult("advanced"),
            [EconomyEffect.Debit(5, "history.bet")],
            [QuotaEffect.Consume("history.daily")],
            [],
            [],
            [ScheduleEffect.Cancel("phase-timeout")]);

        await new TransactionalGameExecutionHistoryCollector().AppendAsync(
            "history:1",
            "history-game",
            "table:1",
            new HistoryCommand("advance"),
            new HistoryState(1),
            decision,
            new EntropyValue([KeyValuePair.Create("roll", 0.25)]),
            Now,
            session,
            CancellationToken.None);
        await session.CommitAsync(CancellationToken.None);

        var reader = new PostgresGameExecutionHistoryReader(new TestConnectionFactory(database.ConnectionString));
        var entry = await reader.GetAsync("history:1");
        var entries = await reader.ListAsync("history-game", "table:1");

        Assert.NotNull(entry);
        Assert.Equal(DecisionStatus.Accepted, entry.DecisionStatus);
        Assert.Equal("history-game", entry.GameId);
        Assert.Equal(0.25, entry.Entropy["roll"]);
        Assert.Contains(nameof(HistoryCommand), entry.Command.TypeName, StringComparison.Ordinal);
        Assert.Equal(1, Revision(entry.PreviousState!.Json));
        Assert.Equal(2, Revision(entry.NextState!.Json));
        Assert.Equal(["economy", "quota", "schedule"], entry.Effects.Select(effect => effect.Category));
        Assert.Single(entries);
    }

    [Fact]
    public async Task RolledBackDecision_DoesNotPublishHistory()
    {
        var factory = new PostgresGameExecutionSessionFactory(new TestConnectionFactory(database.ConnectionString));
        await using var session = await factory.BeginAsync(CancellationToken.None);
        var decision = new GameDecision<HistoryState, HistoryResult>(
            DecisionStatus.Rejected,
            new HistoryState(1),
            new HistoryResult("rejected"),
            [], [], [], [], [], "phase_closed");
        await new TransactionalGameExecutionHistoryCollector().AppendAsync(
            "history:rollback",
            "history-game",
            "table:1",
            new HistoryCommand("rollback"),
            new HistoryState(1),
            decision,
            EntropyValue.Empty,
            Now,
            session,
            CancellationToken.None);
        await session.RollbackAsync(CancellationToken.None);

        var reader = new PostgresGameExecutionHistoryReader(new TestConnectionFactory(database.ConnectionString));
        Assert.Null(await reader.GetAsync("history:rollback"));
    }

    private sealed record HistoryCommand(string Action);

    private sealed record HistoryState(int Revision);

    private sealed record HistoryResult(string Code);

    private static int Revision(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("revision").GetInt32();
    }

    private sealed class TestConnectionFactory(string connectionString) : INpgsqlConnectionFactory
    {
        public NpgsqlConnection Create() => new(connectionString);

        public async Task<NpgsqlConnection> OpenAsync(CancellationToken ct)
        {
            var connection = Create();
            await connection.OpenAsync(ct);
            return connection;
        }
    }
}

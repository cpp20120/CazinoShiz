using System.Data.Common;
using BotFramework.Contracts.Games;
using BotFramework.Host.Execution;
using BotFramework.Sdk.Execution;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CasinoShiz.Tests;

public sealed class HostExecutionPropertyTests
{
    [Property(MaxTest = 100)]
    public Task<Property> Descriptor_DefaultsAreSafe(PositiveInt raw)
    {
        var descriptor = new DefaultDescriptor();
        var command = raw.Get;
        var valid = descriptor.StateType == typeof(TestState)
            && descriptor.ResultType == typeof(int)
            && descriptor.GameId == "property.game"
            && descriptor.CommandId(command) == $"command:{command}"
            && descriptor.AggregateId(command) == $"aggregate:{command}"
            && descriptor.ChatId(command) == command
            && descriptor.DisplayName(command) == $"user:{command}"
            && descriptor.UsesPrimaryWallet
            && descriptor.AdditionalLockKeys(command).Count == 0
            && descriptor.Quotas(command, DateTimeOffset.UnixEpoch).Count == 0
            && descriptor.EntropyNames.Count == 0;

        var initialStateThrows = Assert.Throws<InvalidOperationException>(() => descriptor.CreateInitialState(command));
        return Task.FromResult((valid && initialStateThrows.Message.Contains(nameof(TestState), StringComparison.Ordinal))
            .ToProperty()
            .Label($"command={command}, descriptor={descriptor.GetType().Name}"));
    }

    [Property(MaxTest = 100)]
    public Task<Property> EntropyValue_PreservesUniqueValues(NonNegativeInt raw)
    {
        var values = Enumerable.Range(0, raw.Get % 8)
            .Select(index => new KeyValuePair<string, double>($"roll-{index}", (index + 1) / 100.0))
            .ToArray();
        var entropy = new EntropyValue(values);
        var valid = entropy.Values.Count == values.Length
            && values.All(pair => entropy.GetDouble(pair.Key) == pair.Value)
            && EntropyValue.Empty.Values.Count == 0;
        return Task.FromResult(valid
            .ToProperty()
            .Label($"values={values.Length}"));
    }

    [Property(MaxTest = 100)]
    public Task<Property> LockKeys_AreStableAndScopeSensitive(
        PositiveInt rawUser,
        PositiveInt rawScope,
        NonNegativeInt rawDate)
    {
        var user = rawUser.Get;
        var scope = rawScope.Get;
        var date = new DateOnly(2020 + rawDate.Get % 20, 1 + rawDate.Get % 12, 1 + rawDate.Get % 28);
        var wallet = new WalletIdentity(user, scope);
        var quota = new QuotaIdentity("daily", "dice", user, scope, date, 10);
        var valid = wallet.LockKey == $"wallet:{scope}:{user}"
            && quota.LockKey == $"quota:dice:{scope}:{user}:{date:yyyy-MM-dd}"
            && wallet.LockKey != new WalletIdentity(user + 1, scope).LockKey
            && quota.LockKey != new QuotaIdentity("daily", "dice", user, scope, date.AddDays(1), 10).LockKey;
        return Task.FromResult(valid
            .ToProperty()
            .Label($"user={user}, scope={scope}, date={date:yyyy-MM-dd}"));
    }

    [Property(MaxTest = 100)]
    public async Task<Property> AtomicExecutor_RejectedDecisionCommitsOrRollsBackOnDuplicateQuota(NonNegativeInt raw)
    {
        var command = raw.Get;
        var duplicateQuota = raw.Get % 4 == 0;
        var session = new RecordingSession();
        var inbox = new RecordingInbox();
        var quotas = new RecordingQuotaStore();
        var descriptor = new TestDescriptor(duplicateQuota);
        var executor = new AtomicGameExecutor<int, TestState, int>(
            new RecordingSessionFactory(session),
            inbox,
            new RecordingAvailability(),
            new NoOpEconomics(),
            quotas,
            new NoOpProtection(),
            new RecordingEvents(),
            descriptor,
            new RejectingAction(),
            new RecordingStateStore(),
            [],
            TimeProvider.System,
            new GameExecutionTelemetry(NullLogger<GameExecutionTelemetry>.Instance));

        var exception = await Record.ExceptionAsync(() => executor.ExecuteAsync(
            new GameExecutionEnvelope<int>(command),
            CancellationToken.None));

        var valid = duplicateQuota
            ? exception is InvalidOperationException invalid
                && invalid.Message.Contains("Duplicate quota id", StringComparison.Ordinal)
                && session.RollbackCalls == 1
                && session.CommitCalls == 0
                && !inbox.Completed
            : exception is null
                && inbox.Completed
                && inbox.CompletedResult == command
                && session.CommitCalls == 1
                && session.RollbackCalls == 0
                && quotas.LoadCalls == 1;

        valid &= session.LockKeys.Contains($"command:command:{command}", StringComparer.Ordinal)
            && session.LockKeys.Contains($"game:property.game:aggregate:{command}", StringComparer.Ordinal)
            && session.LockKeys.Contains($"quota:dice:1:{command}:2026-01-01", StringComparer.Ordinal);

        return valid
            .ToProperty()
            .Label($"command={command}, duplicateQuota={duplicateQuota}, exception={exception?.GetType().Name ?? "none"}, locks={session.LockKeys.Count}");
    }

    [Fact]
    public async Task AtomicExecutor_WritesOneExecutionHistoryEntryInsideItsTransaction()
    {
        var session = new RecordingSession();
        var history = new RecordingHistory();
        var executor = new AtomicGameExecutor<int, TestState, int>(
            new RecordingSessionFactory(session),
            new RecordingInbox(),
            new RecordingAvailability(),
            new NoOpEconomics(),
            new RecordingQuotaStore(),
            new NoOpProtection(),
            new RecordingEvents(),
            new TestDescriptor(duplicateQuota: false),
            new RejectingAction(),
            new RecordingStateStore(),
            [],
            TimeProvider.System,
            new GameExecutionTelemetry(NullLogger<GameExecutionTelemetry>.Instance),
            executionHistoryCollector: history);

        await executor.ExecuteAsync(new GameExecutionEnvelope<int>(7), CancellationToken.None);

        Assert.Equal(1, history.AppendCalls);
        Assert.Equal(("command:7", "property.game", "aggregate:7", DecisionStatus.Rejected),
            (history.CommandId, history.GameId, history.AggregateId, history.Status));
        Assert.Equal(1, session.CommitCalls);
    }

    private sealed class DefaultDescriptor : GameExecutionDescriptor<int, TestState, int>
    {
        public override string GameId => "property.game";
        public override string CommandId(int command) => $"command:{command}";
        public override string AggregateId(int command) => $"aggregate:{command}";
        public override long ChatId(int command) => command;
        public override string DisplayName(int command) => $"user:{command}";
        public override WalletIdentity Wallet(int command) => new(command, 1);
    }

    private sealed class TestDescriptor(bool duplicateQuota) : GameExecutionDescriptor<int, TestState, int>
    {
        public override string GameId => "property.game";
        public override string CommandId(int command) => $"command:{command}";
        public override string AggregateId(int command) => $"aggregate:{command}";
        public override long ChatId(int command) => command;
        public override string DisplayName(int command) => $"user:{command}";
        public override WalletIdentity Wallet(int command) => new(command, 1);
        public override bool UsesPrimaryWallet => false;
        public override IReadOnlyList<QuotaIdentity> Quotas(int command, DateTimeOffset utcNow) =>
            duplicateQuota
                ? [Quota(command), Quota(command)]
                : [Quota(command)];
        private static QuotaIdentity Quota(int command) => new("daily", "dice", command, 1, new DateOnly(2026, 1, 1), 10);
    }

    private sealed record TestState(int Value);

    private sealed class RejectingAction : IGameAction<int, TestState, int>
    {
        public GameDecision<TestState, int> Decide(GameActionInput<TestState, int> input) =>
            new(DecisionStatus.Rejected, input.State, input.Command, [], [], [], [], [], "property rejection");
    }

    private sealed class RecordingSessionFactory(RecordingSession session) : IGameExecutionSessionFactory
    {
        public Task<IGameExecutionSession> BeginAsync(CancellationToken ct) => Task.FromResult<IGameExecutionSession>(session);
    }

    private sealed class RecordingSession : IGameExecutionSession
    {
        public DbConnection Connection => null!;
        public DbTransaction Transaction => null!;
        public List<string> LockKeys { get; } = [];
        public int CommitCalls { get; private set; }
        public int RollbackCalls { get; private set; }
        public Task AcquireLocksAsync(IEnumerable<string> lockKeys, CancellationToken ct)
        {
            LockKeys.AddRange(lockKeys);
            return Task.CompletedTask;
        }
        public Task CommitAsync(CancellationToken ct) { CommitCalls++; return Task.CompletedTask; }
        public Task RollbackAsync(CancellationToken ct) { RollbackCalls++; return Task.CompletedTask; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingInbox : ICommandInbox
    {
        public bool Completed { get; private set; }
        public int? CompletedResult { get; private set; }
        public Task<CommandInboxResult<TResult>> GetOrBeginAsync<TResult>(string commandId, string gameId, string aggregateId, IGameExecutionSession session, CancellationToken ct) =>
            Task.FromResult(new CommandInboxResult<TResult>(CommandInboxStatus.New, default));
        public Task CompleteAsync<TResult>(string commandId, TResult result, IGameExecutionSession session, CancellationToken ct)
        {
            Completed = true;
            CompletedResult = result is int value ? value : null;
            return Task.CompletedTask;
        }
        public Task StoreEntropyAsync(string commandId, EntropyValue entropy, IGameExecutionSession session, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class RecordingAvailability : IAtomicGameAvailability
    {
        public Task<GameAvailability> GetAsync(long chatId, string gameId, IGameExecutionSession session, CancellationToken ct) =>
            Task.FromResult(new GameAvailability(chatId, gameId, true, GameAvailabilitySource.Configuration));
    }

    private sealed class RecordingQuotaStore : IAtomicQuotaStore
    {
        public int LoadCalls { get; private set; }
        public Task<QuotaSnapshot> LoadAsync(QuotaIdentity quota, IGameExecutionSession session, CancellationToken ct)
        {
            LoadCalls++;
            return Task.FromResult(new QuotaSnapshot(0, quota.Limit));
        }
        public Task<QuotaSnapshot> ApplyAsync(QuotaIdentity quota, IReadOnlyList<QuotaEffect> effects, IGameExecutionSession session, CancellationToken ct) =>
            Task.FromResult(new QuotaSnapshot(0, quota.Limit));
    }

    private sealed class NoOpEconomics : IAtomicEconomics
    {
        public Task EnsureAsync(WalletIdentity wallet, string displayName, IGameExecutionSession session, CancellationToken ct) => Task.CompletedTask;
        public Task<WalletSnapshot> LoadAsync(WalletIdentity wallet, IGameExecutionSession session, CancellationToken ct) => Task.FromResult(new WalletSnapshot(0));
        public Task<WalletMutationResult> ApplyAsync(WalletIdentity wallet, IReadOnlyList<EconomyEffect> effects, IGameExecutionSession session, string operationId, CancellationToken ct) =>
            Task.FromResult(new WalletMutationResult(true, false, new WalletSnapshot(0)));
    }

    private sealed class NoOpProtection : IAtomicPlayerProtection
    {
        public Task EnforceAsync(long userId, IReadOnlyList<EconomyEffect> effects, IGameExecutionSession session, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class RecordingEvents : ITransactionalEventCollector
    {
        public Task AppendAsync(string commandId, IReadOnlyList<BotFramework.Sdk.Events.Contracts.IDomainEvent> events, IGameExecutionSession session, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class RecordingStateStore : IGameStateStore<int, TestState>
    {
        public Task<TestState> LoadAsync(int command, IGameExecutionContext context, CancellationToken ct) => Task.FromResult(new TestState(command));
        public Task SaveAsync(int command, TestState state, IGameExecutionContext context, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class RecordingHistory : ITransactionalGameExecutionHistoryCollector
    {
        public int AppendCalls { get; private set; }
        public string? CommandId { get; private set; }
        public string? GameId { get; private set; }
        public string? AggregateId { get; private set; }
        public DecisionStatus? Status { get; private set; }

        public Task AppendAsync<TCommand, TState, TResult>(
            string commandId,
            string gameId,
            string aggregateId,
            TCommand command,
            TState currentState,
            GameDecision<TState, TResult> decision,
            EntropyValue entropy,
            DateTimeOffset occurredAt,
            IGameExecutionSession session,
            CancellationToken ct)
        {
            AppendCalls++;
            CommandId = commandId;
            GameId = gameId;
            AggregateId = aggregateId;
            Status = decision.Status;
            return Task.CompletedTask;
        }
    }
}

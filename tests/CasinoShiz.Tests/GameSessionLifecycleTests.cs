using BotFramework.Host.Execution.Lifecycle;
using BotFramework.Sdk.Execution.Lifecycle;
using Xunit;

namespace CasinoShiz.Tests;

public sealed class GameSessionLifecycleTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Lifecycle_IsTransportNeutralResumableAndCorrelationIdempotent()
    {
        var service = CreateService();
        var start = new GameSessionStartRequest(
            GameId: "blackjack",
            OwnerId: "player:42",
            ScopeId: "table:main",
            CorrelationId: "start:42",
            Data: """{ "hand": 1 }""");

        var started = await service.StartAsync(start, CancellationToken.None);
        var replayedStart = await service.StartAsync(start, CancellationToken.None);
        var suspended = await service.SuspendAsync(
            new(started.SessionId, "suspend:42", started.Revision),
            CancellationToken.None);
        var resumed = await service.ResumeAsync(
            new(suspended.SessionId, "resume:42", suspended.Revision, """{ "hand": 2 }"""),
            CancellationToken.None);
        var completed = await service.CompleteAsync(
            new(resumed.SessionId, "complete:42", resumed.Revision),
            CancellationToken.None);

        Assert.Equal(started, replayedStart);
        Assert.Equal((GameSessionLifecycle.Started, 0L, "start:42", "start:42"),
            (started.Lifecycle, started.Revision, started.RootCorrelationId, started.LastCorrelationId));
        Assert.Equal((GameSessionLifecycle.Suspended, 1L), (suspended.Lifecycle, suspended.Revision));
        Assert.Equal((GameSessionLifecycle.Resumed, 2L, "start:42", "resume:42", "{ \"hand\": 2 }"),
            (resumed.Lifecycle, resumed.Revision, resumed.RootCorrelationId, resumed.LastCorrelationId, resumed.Data));
        Assert.Equal((GameSessionLifecycle.Completed, 3L, true),
            (completed.Lifecycle, completed.Revision, completed.IsTerminal));

        var foundByFirstCorrelation = await service.GetByCorrelationAsync("start:42", CancellationToken.None);
        Assert.Equal(completed, foundByFirstCorrelation);
    }

    [Fact]
    public async Task Transition_RetriesReturnTheRecordedSnapshot_AndRejectStaleRevision()
    {
        var service = CreateService();
        var started = await service.StartAsync(
            new("poker", "player:42", "room:green", "start"),
            CancellationToken.None);
        var suspended = await service.SuspendAsync(
            new(started.SessionId, "suspend", started.Revision),
            CancellationToken.None);
        var replayed = await service.SuspendAsync(
            new(started.SessionId, "suspend", started.Revision),
            CancellationToken.None);

        Assert.Equal(suspended, replayed);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ResumeAsync(
            new(started.SessionId, "resume", started.Revision),
            CancellationToken.None));
    }

    [Fact]
    public async Task Lifecycle_OnlyAllowsValidTransitionsAndFailureRequiresCode()
    {
        var service = CreateService();
        var started = await service.StartAsync(
            new("pick", "player:7", "scope:day", "start"),
            CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ResumeAsync(
            new(started.SessionId, "resume", started.Revision),
            CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => service.FailAsync(
            new(started.SessionId, "fail", started.Revision),
            CancellationToken.None));

        var failed = await service.FailAsync(
            new(started.SessionId, "fail:reason", started.Revision, FailureCode: "player_left"),
            CancellationToken.None);
        Assert.Equal((GameSessionLifecycle.Failed, "player_left", true),
            (failed.Lifecycle, failed.FailureCode, failed.IsTerminal));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CompleteAsync(
            new(failed.SessionId, "complete", failed.Revision),
            CancellationToken.None));
    }

    [Fact]
    public async Task ExpiredSuspendedSession_CannotBeResumed()
    {
        var clock = new FixedTimeProvider(Now);
        var service = new DefaultGameSessionService(new InMemoryGameSessionStore(), clock);
        var started = await service.StartAsync(
            new("chess", "player:1", "scope:1", "start", ExpiresAt: Now.AddMinutes(1)),
            CancellationToken.None);
        var suspended = await service.SuspendAsync(
            new(started.SessionId, "suspend", started.Revision),
            CancellationToken.None);
        clock.UtcNow = Now.AddMinutes(2);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ResumeAsync(
            new(suspended.SessionId, "resume", suspended.Revision),
            CancellationToken.None));
    }

    [Fact]
    public async Task SuspendedSession_CanCompleteFromAnExternalCallback()
    {
        var service = CreateService();
        var started = await service.StartAsync(
            new("remote-match", "player:9", "lobby:one", "start"),
            CancellationToken.None);
        var suspended = await service.SuspendAsync(
            new(started.SessionId, "wait-for-result", started.Revision),
            CancellationToken.None);

        var completed = await service.CompleteAsync(
            new(suspended.SessionId, "result-received", suspended.Revision),
            CancellationToken.None);

        Assert.Equal((GameSessionLifecycle.Completed, 2L), (completed.Lifecycle, completed.Revision));
    }

    private static DefaultGameSessionService CreateService() =>
        new(new InMemoryGameSessionStore(), new FixedTimeProvider(Now));

    private sealed class InMemoryGameSessionStore : IGameSessionStore
    {
        private readonly Dictionary<string, GameSession> sessions = [];
        private readonly Dictionary<(string SessionId, string CorrelationId), GameSession> snapshots = [];
        private readonly Dictionary<string, string> correlations = [];

        public Task<GameSession?> GetAsync(string sessionId, CancellationToken ct) =>
            Task.FromResult(sessions.GetValueOrDefault(sessionId));

        public Task<GameSession?> GetByCorrelationAsync(string correlationId, CancellationToken ct) =>
            Task.FromResult(correlations.TryGetValue(correlationId, out var sessionId)
                ? sessions.GetValueOrDefault(sessionId)
                : null);

        public Task<GameSession?> GetSnapshotByCorrelationAsync(
            string sessionId,
            string correlationId,
            CancellationToken ct) =>
            Task.FromResult(snapshots.GetValueOrDefault((sessionId, correlationId)));

        public Task<GameSessionWriteResult> TryStartAsync(
            GameSession session,
            GameSessionLifecycleRecord lifecycle,
            CancellationToken ct)
        {
            if (snapshots.TryGetValue((session.SessionId, lifecycle.CorrelationId), out var replayed))
            {
                return Task.FromResult(new GameSessionWriteResult(GameSessionWriteStatus.AlreadyApplied, replayed));
            }

            if (sessions.TryGetValue(session.SessionId, out var existing))
            {
                return Task.FromResult(new GameSessionWriteResult(GameSessionWriteStatus.SessionAlreadyExists, existing));
            }

            sessions.Add(session.SessionId, session);
            snapshots.Add((session.SessionId, lifecycle.CorrelationId), session);
            correlations.Add(lifecycle.CorrelationId, session.SessionId);
            return Task.FromResult(new GameSessionWriteResult(GameSessionWriteStatus.Applied, session));
        }

        public Task<GameSessionWriteResult> TryTransitionAsync(
            GameSession current,
            GameSession updated,
            GameSessionLifecycleRecord lifecycle,
            CancellationToken ct)
        {
            if (snapshots.TryGetValue((updated.SessionId, lifecycle.CorrelationId), out var replayed))
            {
                return Task.FromResult(new GameSessionWriteResult(GameSessionWriteStatus.AlreadyApplied, replayed));
            }

            if (!sessions.TryGetValue(updated.SessionId, out var stored) || stored.Revision != current.Revision)
            {
                return Task.FromResult(new GameSessionWriteResult(GameSessionWriteStatus.RevisionConflict, current));
            }

            sessions[updated.SessionId] = updated;
            snapshots.Add((updated.SessionId, lifecycle.CorrelationId), updated);
            correlations.Add(lifecycle.CorrelationId, updated.SessionId);
            return Task.FromResult(new GameSessionWriteResult(GameSessionWriteStatus.Applied, updated));
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}

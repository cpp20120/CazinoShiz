using BotFramework.Sdk.Execution.Lifecycle;

namespace BotFramework.Host.Execution.Lifecycle;

/// <summary>
/// Coordinates durable game-session lifecycle changes. The service deliberately
/// knows nothing about an inbound transport: owner, scope and correlation ids
/// are opaque values supplied by an adapter or an application service.
/// </summary>
public sealed class DefaultGameSessionService(
    IGameSessionStore store,
    TimeProvider timeProvider) : IGameSessionService
{
    public async Task<GameSession> StartAsync(GameSessionStartRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = timeProvider.GetUtcNow();
        var correlationId = GameSessionData.RequireId(request.CorrelationId, nameof(request.CorrelationId));
        var existing = await store.GetByCorrelationAsync(correlationId, ct);
        if (existing is not null)
        {
            return existing;
        }
        var session = new GameSession(
            request.SessionId is null ? Guid.NewGuid().ToString("N") : GameSessionData.RequireId(request.SessionId, nameof(request.SessionId)),
            GameSessionData.RequireId(request.GameId, nameof(request.GameId)),
            GameSessionData.RequireId(request.OwnerId, nameof(request.OwnerId)),
            GameSessionData.RequireId(request.ScopeId, nameof(request.ScopeId)),
            correlationId,
            correlationId,
            GameSessionLifecycle.Started,
            Revision: 0,
            GameSessionData.Normalize(request.Data),
            FailureCode: null,
            StartedAt: now,
            UpdatedAt: now,
            request.ExpiresAt);
        var lifecycle = new GameSessionLifecycleRecord(
            session.SessionId,
            session.RootCorrelationId,
            session.Lifecycle,
            session.Revision,
            FailureCode: null,
            now);
        var result = await store.TryStartAsync(session, lifecycle, ct);

        return result.Status switch
        {
            GameSessionWriteStatus.Applied or GameSessionWriteStatus.AlreadyApplied => result.Session,
            GameSessionWriteStatus.SessionAlreadyExists => throw new InvalidOperationException(
                $"Game session '{session.SessionId}' already exists with another correlation id."),
            _ => throw new InvalidOperationException($"Could not start game session '{session.SessionId}'."),
        };
    }

    public Task<GameSession> SuspendAsync(GameSessionTransitionRequest request, CancellationToken ct) =>
        TransitionAsync(request, GameSessionLifecycle.Suspended, ct);

    public Task<GameSession> ResumeAsync(GameSessionTransitionRequest request, CancellationToken ct) =>
        TransitionAsync(request, GameSessionLifecycle.Resumed, ct);

    public Task<GameSession> CompleteAsync(GameSessionTransitionRequest request, CancellationToken ct) =>
        TransitionAsync(request, GameSessionLifecycle.Completed, ct);

    public Task<GameSession> FailAsync(GameSessionTransitionRequest request, CancellationToken ct) =>
        TransitionAsync(request, GameSessionLifecycle.Failed, ct);

    public Task<GameSession?> GetAsync(string sessionId, CancellationToken ct) =>
        store.GetAsync(GameSessionData.RequireId(sessionId, nameof(sessionId)), ct);

    public Task<GameSession?> GetByCorrelationAsync(string correlationId, CancellationToken ct) =>
        store.GetByCorrelationAsync(GameSessionData.RequireId(correlationId, nameof(correlationId)), ct);

    private async Task<GameSession> TransitionAsync(
        GameSessionTransitionRequest request,
        GameSessionLifecycle target,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var sessionId = GameSessionData.RequireId(request.SessionId, nameof(request.SessionId));
        var correlationId = GameSessionData.RequireId(request.CorrelationId, nameof(request.CorrelationId));

        var applied = await store.GetSnapshotByCorrelationAsync(sessionId, correlationId, ct);
        if (applied is not null)
        {
            return applied;
        }

        var current = await store.GetAsync(sessionId, ct)
            ?? throw new KeyNotFoundException($"Game session '{sessionId}' was not found.");
        if (current.Revision != request.ExpectedRevision)
        {
            throw new InvalidOperationException(
                $"Game session '{sessionId}' has revision {current.Revision}; expected {request.ExpectedRevision}.");
        }

        var now = timeProvider.GetUtcNow();
        EnsureTransitionAllowed(current, target, now);
        var failureCode = target == GameSessionLifecycle.Failed
            ? GameSessionData.RequireId(request.FailureCode ?? string.Empty, nameof(request.FailureCode))
            : null;
        var next = current with
        {
            Lifecycle = target,
            Revision = current.Revision + 1,
            LastCorrelationId = correlationId,
            Data = request.Data is null ? current.Data : GameSessionData.Normalize(request.Data),
            FailureCode = failureCode,
            UpdatedAt = now,
            ExpiresAt = request.ExpiresAt ?? current.ExpiresAt,
        };
        var lifecycle = new GameSessionLifecycleRecord(
            next.SessionId,
            correlationId,
            target,
            next.Revision,
            failureCode,
            now);
        var result = await store.TryTransitionAsync(current, next, lifecycle, ct);

        return result.Status switch
        {
            GameSessionWriteStatus.Applied or GameSessionWriteStatus.AlreadyApplied => result.Session,
            GameSessionWriteStatus.RevisionConflict => throw new InvalidOperationException(
                $"Game session '{sessionId}' was changed concurrently; reload it before retrying."),
            _ => throw new InvalidOperationException($"Could not transition game session '{sessionId}'."),
        };
    }

    private static void EnsureTransitionAllowed(
        GameSession current,
        GameSessionLifecycle target,
        DateTimeOffset now)
    {
        if (current.IsTerminal)
        {
            throw new InvalidOperationException($"Terminal game session '{current.SessionId}' cannot transition again.");
        }

        if (current.ExpiresAt is { } expiresAt && expiresAt <= now && target == GameSessionLifecycle.Resumed)
        {
            throw new InvalidOperationException($"Game session '{current.SessionId}' has expired and cannot be resumed.");
        }

        var allowed = current.Lifecycle switch
        {
            GameSessionLifecycle.Started or GameSessionLifecycle.Resumed =>
                target is GameSessionLifecycle.Suspended or GameSessionLifecycle.Completed or GameSessionLifecycle.Failed,
            GameSessionLifecycle.Suspended =>
                target is GameSessionLifecycle.Resumed or GameSessionLifecycle.Completed or GameSessionLifecycle.Failed,
            _ => false,
        };
        if (!allowed)
        {
            throw new InvalidOperationException(
                $"Game session '{current.SessionId}' cannot transition from {current.Lifecycle} to {target}.");
        }
    }
}

namespace BotFramework.Sdk.Execution.Lifecycle;

/// <summary>Persistence port for durable game sessions and correlation lookups.</summary>
public interface IGameSessionStore
{
    Task<GameSession?> GetAsync(string sessionId, CancellationToken ct);

    Task<GameSession?> GetByCorrelationAsync(string correlationId, CancellationToken ct);

    Task<GameSession?> GetSnapshotByCorrelationAsync(string sessionId, string correlationId, CancellationToken ct);

    Task<GameSessionWriteResult> TryStartAsync(
        GameSession session,
        GameSessionLifecycleRecord lifecycle,
        CancellationToken ct);

    Task<GameSessionWriteResult> TryTransitionAsync(
        GameSession current,
        GameSession updated,
        GameSessionLifecycleRecord lifecycle,
        CancellationToken ct);
}

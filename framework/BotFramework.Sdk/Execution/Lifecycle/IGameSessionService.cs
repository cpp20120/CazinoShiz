namespace BotFramework.Sdk.Execution.Lifecycle;

/// <summary>Application-facing lifecycle API for transport adapters and game services.</summary>
public interface IGameSessionService
{
    Task<GameSession> StartAsync(GameSessionStartRequest request, CancellationToken ct);

    Task<GameSession> SuspendAsync(GameSessionTransitionRequest request, CancellationToken ct);

    Task<GameSession> ResumeAsync(GameSessionTransitionRequest request, CancellationToken ct);

    Task<GameSession> CompleteAsync(GameSessionTransitionRequest request, CancellationToken ct);

    Task<GameSession> FailAsync(GameSessionTransitionRequest request, CancellationToken ct);

    Task<GameSession?> GetAsync(string sessionId, CancellationToken ct);

    Task<GameSession?> GetByCorrelationAsync(string correlationId, CancellationToken ct);
}

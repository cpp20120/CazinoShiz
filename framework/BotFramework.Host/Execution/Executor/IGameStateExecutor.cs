using BotFramework.Sdk.Execution;

namespace BotFramework.Host.Execution;

/// <summary>
/// Commits a game-state transition and its outbox effects without touching a wallet,
/// wagering reservation, quota, or player-protection boundary.
/// </summary>
public interface IGameStateExecutor<TCommand, TState, TResult>
{
    Type StateType { get; }

    Task<TResult> ExecuteAsync(GameExecutionEnvelope<TCommand> envelope, CancellationToken ct);
}

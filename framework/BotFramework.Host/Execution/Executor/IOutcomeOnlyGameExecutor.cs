using BotFramework.Sdk.Execution;

namespace BotFramework.Host.Execution;

/// <summary>
/// Executes a game state transition without acquiring or mutating the primary
/// wallet. The descriptor must set UsesPrimaryWallet=false and the action must
/// emit only game state/events; wagering settlement happens outside the game.
/// </summary>
public interface IOutcomeOnlyGameExecutor<TCommand, TState, TResult>
{
    Task<TResult> ExecuteAsync(GameExecutionEnvelope<TCommand> envelope, CancellationToken ct);
}

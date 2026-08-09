using BotFramework.Sdk.Execution;

namespace BotFramework.Host.Execution;

/// <summary>Public adapter for game execution that is isolated from wagering settlement.</summary>
public sealed class OutcomeOnlyGameExecutor<TCommand, TState, TResult>(
    IGameStateExecutor<TCommand, TState, TResult> inner)
    : IOutcomeOnlyGameExecutor<TCommand, TState, TResult>
{
    public Task<TResult> ExecuteAsync(GameExecutionEnvelope<TCommand> envelope, CancellationToken ct) =>
        inner.ExecuteAsync(envelope, ct);
}

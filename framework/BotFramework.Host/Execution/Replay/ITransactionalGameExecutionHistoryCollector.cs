using BotFramework.Sdk.Execution;

namespace BotFramework.Host.Execution;

/// <summary>Transaction-bound writer for the framework execution journal.</summary>
internal interface ITransactionalGameExecutionHistoryCollector
{
    Task AppendAsync<TCommand, TState, TResult>(
        string commandId,
        string gameId,
        string aggregateId,
        TCommand command,
        TState currentState,
        GameDecision<TState, TResult> decision,
        EntropyValue entropy,
        DateTimeOffset occurredAt,
        IGameExecutionSession session,
        CancellationToken ct);
}

using BotFramework.Scheduling.Abstractions;
using BotFramework.Sdk.Execution;

namespace BotFramework.Host.Execution;

public sealed class GameStateScheduledCommand<TCommand, TState, TResult>(
    IGameStateExecutor<TCommand, TState, TResult> executor) : IScheduledCommand
{
    public string Key => AtomicGameSchedule.JobKey<TCommand>();

    public Task ExecuteAsync(IReadOnlyDictionary<string, string> data, CancellationToken ct) =>
        executor.ExecuteAsync(
            new GameExecutionEnvelope<TCommand>(AtomicGameSchedule.DeserializeCommand<TCommand>(data))
            {
                TenantContext = AtomicGameSchedule.TryGetTenantContext(data, out var context)
                    ? context
                    : null,
            },
            ct);
}

using BotFramework.Sdk.Execution;

namespace BotFramework.Host.Execution;

/// <summary>Framework state store for one-shot atomic games that retain no aggregate state.</summary>
public sealed class StatelessGameStateStore<TCommand> : IGameStateStore<TCommand, NoGameState>
{
    public Task<NoGameState> LoadAsync(
        TCommand command,
        IGameExecutionContext context,
        CancellationToken ct) => Task.FromResult(default(NoGameState));

    public Task SaveAsync(
        TCommand command,
        NoGameState state,
        IGameExecutionContext context,
        CancellationToken ct) => Task.CompletedTask;
}

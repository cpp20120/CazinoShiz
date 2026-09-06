using BotFramework.Sdk.Execution;

namespace BotFramework.Host.Execution;

internal interface ITransactionalGameInputRequestStore
{
    Task CreateAsync(
        InputRequestEffect effect,
        IGameExecutionContext context,
        CancellationToken ct);
}

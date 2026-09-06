using BotFramework.Sdk.Execution;

namespace BotFramework.Host.Execution;

internal interface ITransactionalGameEffectOutbox
{
    Task AppendAsync(
        string commandId,
        string gameId,
        string aggregateId,
        IReadOnlyList<(int EffectIndex, IDurableGameEffect Effect)> effects,
        IGameExecutionContext context,
        IGameExecutionSession session,
        CancellationToken ct);
}

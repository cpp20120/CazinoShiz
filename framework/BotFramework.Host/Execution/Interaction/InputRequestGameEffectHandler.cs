using BotFramework.Sdk.Execution;

namespace BotFramework.Host.Execution;

internal sealed class InputRequestGameEffectHandler(ITransactionalGameInputRequestStore store)
    : GameEffectHandler<InputRequestEffect>
{
    protected override async Task ApplyBatchAsync(
        IReadOnlyList<InputRequestEffect> effects,
        IGameExecutionContext context,
        CancellationToken ct)
    {
        foreach (var effect in effects)
            await store.CreateAsync(effect, context, ct);
    }
}

using BotFramework.Contracts.Tenancy;
using BotFramework.Sdk.Execution;

namespace BotFramework.Host.Execution;

internal sealed class GameStateEffectPipeline<TCommand, TState, TResult>(
    ITransactionalEventCollector eventCollector,
    IGameStateStore<TCommand, TState> stateStore,
    IEnumerable<IGameRecordWriter> recordWriters,
    ITransactionalScheduleCollector? scheduleCollector,
    IEnumerable<IGameEffectHandler>? effectHandlers = null)
{
    private readonly Dictionary<Type, IGameRecordWriter> writers = recordWriters
        .GroupBy(writer => writer.RecordType)
        .ToDictionary(
            group => group.Key,
            group => group.Count() == 1
                ? group.Single()
                : throw new InvalidOperationException($"Multiple game record writers are registered for '{group.Key}'."));
    private readonly Dictionary<Type, IGameEffectHandler> handlers = (effectHandlers ?? [])
        .GroupBy(handler => handler.EffectType)
        .ToDictionary(
            group => group.Key,
            group => group.Count() == 1
                ? group.Single()
                : throw new InvalidOperationException($"Multiple game effect handlers are registered for '{group.Key}'."));

    public GameEffectPlan Plan(GameDecision<TState, TResult> decision)
    {
        ArgumentNullException.ThrowIfNull(decision);
        var plan = GameEffectPlan.Create(decision, [], writers, handlers);
        if (plan.Effects.Economy.Count != 0 || plan.Effects.Quotas.Count != 0)
        {
            throw new InvalidOperationException(
                "A state-only game decision cannot contain economy or quota effects. " +
                "Settlement belongs to the wagering workflow.");
        }
        if (plan.Effects.Custom.Any(static effect =>
                effect is WalletEconomyEffect or TenantWalletEconomyEffect))
        {
            throw new InvalidOperationException(
                "A state-only game decision cannot contain wallet custom effects. " +
                "Settlement belongs to the wagering workflow.");
        }

        return plan;
    }

    public async Task ApplyAsync(
        string commandId,
        string gameId,
        string aggregateId,
        TCommand command,
        TState currentState,
        GameDecision<TState, TResult> decision,
        GameEffectPlan plan,
        IGameExecutionContext executionContext,
        IGameExecutionSession session,
        TenantContext? tenantContext,
        CancellationToken ct)
    {
        if (decision.Status == DecisionStatus.Accepted)
            await SaveStateAsync(command, currentState, decision.NewState, executionContext, ct);

        foreach (var (record, writer) in plan.Records)
            await writer.WriteAsync(record, executionContext, ct);

        foreach (var (handler, effects) in plan.Custom)
            await handler.ApplyAsync(effects, executionContext, ct);

        await eventCollector.AppendAsync(
            commandId,
            plan.Effects.Events,
            session,
            tenantContext,
            ct);

        if (plan.Effects.Schedules.Count == 0)
            return;

        var collector = scheduleCollector
            ?? throw new InvalidOperationException("Schedule collector is required for scheduled effects.");
        await collector.AppendAsync(
            commandId,
            gameId,
            aggregateId,
            plan.Effects.Schedules,
            session,
            tenantContext,
            ct);
    }

    private Task SaveStateAsync(
        TCommand command,
        TState currentState,
        TState newState,
        IGameExecutionContext executionContext,
        CancellationToken ct)
    {
        if (currentState is IVersionedGameState currentVersioned)
        {
            if (newState is not IVersionedGameState newVersioned)
                throw new InvalidOperationException("A versioned aggregate cannot become unversioned.");
            if (newVersioned.Revision != checked(currentVersioned.Revision + 1))
                throw new InvalidOperationException("An accepted decision must advance aggregate revision exactly once.");
            if (stateStore is not IVersionedGameStateStore<TCommand, TState> versionedStore)
            {
                throw new InvalidOperationException(
                    $"State store '{stateStore.GetType().Name}' does not support versioned aggregate saves.");
            }
            return versionedStore.SaveVersionedAsync(
                command,
                newState,
                currentVersioned.Revision,
                executionContext,
                ct);
        }

        if (newState is IVersionedGameState)
            throw new InvalidOperationException("An unversioned aggregate cannot become versioned during a decision.");
        return stateStore.SaveAsync(command, newState, executionContext, ct);
    }
}

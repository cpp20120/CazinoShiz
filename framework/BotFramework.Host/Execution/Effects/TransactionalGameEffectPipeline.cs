using BotFramework.Sdk.Execution;

namespace BotFramework.Host.Execution;

internal sealed class TransactionalGameEffectPipeline<TCommand, TState, TResult>(
    IAtomicEconomics economics,
    IAtomicQuotaStore quotaStore,
    IAtomicPlayerProtection playerProtection,
    ITransactionalEventCollector eventCollector,
    IGameStateStore<TCommand, TState> stateStore,
    IEnumerable<IGameRecordWriter> recordWriters,
    ITransactionalScheduleCollector? scheduleCollector,
    IEnumerable<IGameEffectHandler>? effectHandlers = null,
    IGameCapabilityValidator? capabilityValidator = null,
    ITransactionalGameEffectOutbox? effectOutbox = null)
{
    private readonly Dictionary<Type, IGameRecordWriter> _writers = recordWriters
        .GroupBy(writer => writer.RecordType)
        .ToDictionary(
            group => group.Key,
            group => group.Count() == 1
                ? group.Single()
                : throw new InvalidOperationException($"Multiple game record writers are registered for '{group.Key}'."));
    private readonly Dictionary<Type, IGameEffectHandler> _handlers = (effectHandlers ?? [])
        .GroupBy(handler => handler.EffectType)
        .ToDictionary(
            group => group.Key,
            group => group.Count() == 1
                ? group.Single()
                : throw new InvalidOperationException($"Multiple game effect handlers are registered for '{group.Key}'."));

    public GameEffectPlan Plan(
        GameDecision<TState, TResult> decision,
        IReadOnlyList<QuotaIdentity> quotas) =>
        Plan(decision, quotas, GameCapabilitySet.Empty);

    public GameEffectPlan Plan(
        GameDecision<TState, TResult> decision,
        IReadOnlyList<QuotaIdentity> quotas,
        GameCapabilitySet requiredCapabilities)
    {
        var plan = GameEffectPlan.Create(
            decision,
            quotas,
            _writers,
            _handlers,
            requiredCapabilities,
            capabilityValidator);
        if (plan.Effects.Schedules.Count != 0 && scheduleCollector is null)
            throw new InvalidOperationException("No transactional schedule collector is registered.");
        return plan;
    }

    public async Task ApplyAsync(
        string commandId,
        string gameId,
        string aggregateId,
        TCommand command,
        TState currentState,
        WalletIdentity wallet,
        IReadOnlyList<QuotaIdentity> quotas,
        GameDecision<TState, TResult> decision,
        GameEffectPlan plan,
        IGameExecutionContext executionContext,
        IGameExecutionSession session,
        CancellationToken ct)
    {
        if (decision.Status == DecisionStatus.Accepted)
        {
            await ApplyAcceptedAsync(
                commandId,
                command,
                currentState,
                wallet,
                quotas,
                decision,
                plan,
                executionContext,
                session,
                ct);
        }

        foreach (var (record, writer) in plan.Records)
            await writer.WriteAsync(record, executionContext, ct);

        foreach (var (handler, effects) in plan.Custom)
        {
            var immediateEffects = effects.Where(static effect => effect is not IDurableGameEffect).ToArray();
            if (immediateEffects.Length != 0)
                await handler.ApplyAsync(immediateEffects, executionContext, ct);
        }

        if (plan.DurableEffects.Count != 0)
        {
            var durableOutbox = effectOutbox ?? throw new InvalidOperationException(
                "A durable game effect was emitted but no transactional game effect outbox is registered.");
            await durableOutbox.AppendAsync(
                commandId,
                gameId,
                aggregateId,
                plan.DurableEffects,
                executionContext,
                session,
                ct);
        }

        await eventCollector.AppendAsync(
            commandId,
            plan.Effects.Events,
            session,
            executionContext.TenantContext,
            ct);
        if (plan.Effects.Schedules.Count != 0)
        {
            var collector = scheduleCollector
                ?? throw new InvalidOperationException("Schedule collector is required for scheduled effects.");
            await collector.AppendAsync(
                commandId,
                gameId,
                aggregateId,
                plan.Effects.Schedules,
                session,
                executionContext.TenantContext,
                ct);
        }
    }

    private async Task ApplyAcceptedAsync(
        string commandId,
        TCommand command,
        TState currentState,
        WalletIdentity wallet,
        IReadOnlyList<QuotaIdentity> quotas,
        GameDecision<TState, TResult> decision,
        GameEffectPlan plan,
        IGameExecutionContext executionContext,
        IGameExecutionSession session,
        CancellationToken ct)
    {
        if (plan.Effects.Economy.Count != 0)
        {
            await playerProtection.EnforceAsync(wallet.UserId, plan.Effects.Economy, session, ct);
            var walletMutation = await economics.ApplyAsync(
                wallet,
                plan.Effects.Economy,
                session,
                $"{commandId}:primary-wallet",
                ct);
            if (walletMutation.Rejected)
                throw new InvalidOperationException("A decision accepted an economy mutation that the locked wallet rejected.");
        }

        foreach (var quota in quotas)
        {
            var effects = plan.QuotaEffects.GetValueOrDefault(quota.QuotaId, []);
            await quotaStore.ApplyAsync(quota, effects, session, ct);
        }

        await SaveStateAsync(command, currentState, decision.NewState, executionContext, ct);
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

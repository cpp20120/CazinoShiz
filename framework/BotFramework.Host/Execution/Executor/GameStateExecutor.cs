using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using BotFramework.Contracts.Messaging;
using BotFramework.Contracts.Tenancy;
using BotFramework.Sdk.Execution;

namespace BotFramework.Host.Execution;

internal sealed class GameStateExecutor<TCommand, TState, TResult>(
    IGameExecutionSessionFactory sessions,
    ICommandInbox inbox,
    IAtomicGameAvailability availability,
    ITransactionalEventCollector eventCollector,
    GameExecutionDescriptor<TCommand, TState, TResult> descriptor,
    IGameAction<TCommand, TState, TResult> action,
    IGameStateStore<TCommand, TState> stateStore,
    IEnumerable<IGameRecordWriter> recordWriters,
    TimeProvider timeProvider,
    GameExecutionTelemetry telemetry,
    ITransactionalScheduleCollector? scheduleCollector = null,
    IEnumerable<IGameEffectHandler>? effectHandlers = null,
    ITenantContextProvisioner? tenantContextProvisioner = null,
    ITenantContextAccessor? tenantContextAccessor = null,
    IGameCapabilityValidator? capabilityValidator = null,
    ITransactionalGameEffectOutbox? effectOutbox = null,
    ITransactionalGameExecutionHistoryCollector? executionHistoryCollector = null)
    : IGameStateExecutor<TCommand, TState, TResult>
{
    private readonly GameStateEffectPipeline<TCommand, TState, TResult> effectPipeline = new(
        eventCollector,
        stateStore,
        recordWriters,
        scheduleCollector,
        effectHandlers,
        capabilityValidator,
        effectOutbox);

    public Type StateType => typeof(TState);

    public async Task<TResult> ExecuteAsync(GameExecutionEnvelope<TCommand> envelope, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        var command = envelope.Command;
        var tenantContext = envelope.TenantContext
            ?? RequestMetadataContext.TryGetCurrent()?.TenantContext;
        var channel = tenantContext?.Channel
            ?? RequestMetadataContext.TryGetCurrent()?.Channel
            ?? BotChannel.System;
        using var metadataScope = tenantContext is { } context
            ? RequestMetadataContext.Push(RequestMetadata.FromTenantContext(context, "sdk"))
            : null;
        using var tenantScope = tenantContext is { } scopedTenant && tenantContextAccessor is not null
            ? tenantContextAccessor.Push(scopedTenant)
            : null;
        if (tenantContext is not null && tenantContextProvisioner is not null)
            await tenantContextProvisioner.EnsureAsync(tenantContext, ct);

        if (descriptor.UsesPrimaryWallet)
        {
            throw new InvalidOperationException(
                $"State-only executor cannot run descriptor '{descriptor.GetType().Name}' with UsesPrimaryWallet=true.");
        }

        var commandId = descriptor.CommandId(command);
        var aggregateId = descriptor.AggregateId(command);
        var chatId = descriptor.ChatId(command);
        var utcNow = timeProvider.GetUtcNow();
        if (descriptor.Quotas(command, utcNow).Count != 0)
        {
            throw new InvalidOperationException(
                $"State-only executor cannot run descriptor '{descriptor.GetType().Name}' with quota effects enabled.");
        }
        using var observation = telemetry.Start(descriptor.GameId, commandId, aggregateId);

        IGameExecutionSession? session = null;
        var transactionStartedAt = 0L;
        var committed = false;
        try
        {
            session = await sessions.BeginAsync(ct);
            transactionStartedAt = Stopwatch.GetTimestamp();
            observation.LockWaitStarted();
            var lockStartedAt = Stopwatch.GetTimestamp();
            await session.AcquireLocksAsync(
                BuildLockKeys(command, commandId, aggregateId, tenantContext),
                ct);
            observation.Locked(Stopwatch.GetElapsedTime(lockStartedAt));

            var existing = await inbox.GetOrBeginAsync<TResult>(
                commandId,
                descriptor.GameId,
                aggregateId,
                session,
                ct);
            if (existing.Status == CommandInboxStatus.Completed)
            {
                observation.Duplicate();
                observation.Committing();
                await session.CommitAsync(ct);
                committed = true;
                observation.Committed();
                return existing.Result!;
            }

            var availabilityState = await availability.GetAsync(
                chatId,
                descriptor.GameId,
                session,
                ct);
            if (!availabilityState.Enabled)
                throw new GameUnavailableException(descriptor.GameId, chatId, availabilityState.Reason);

            if (action is IGameCommandPreflight<TCommand, TResult> preflight
                && preflight.TryReject(command, out var preflightResult, out var preflightReason))
            {
                var preflightEntropy = CreateEntropy(descriptor.EntropyNames);
                await inbox.StoreEntropyAsync(commandId, preflightEntropy, session, ct);
                observation.Decided(DecisionStatus.Rejected, preflightReason);
                await inbox.CompleteAsync(commandId, preflightResult, session, ct);
                observation.Committing();
                await session.CommitAsync(ct);
                committed = true;
                observation.Committed();
                return preflightResult;
            }

            var executionContext = new GameExecutionContext(
                session,
                operationId: commandId,
                tenantContext: tenantContext,
                gameId: descriptor.GameId,
                aggregateId: aggregateId);
            var state = await stateStore.LoadAsync(command, executionContext, ct);
            var entropy = CreateEntropy(descriptor.EntropyNames);
            await inbox.StoreEntropyAsync(commandId, entropy, session, ct);
            var input = new GameActionInput<TState, TCommand>(
                command,
                state,
                new WalletSnapshot(0),
                new Dictionary<string, QuotaSnapshot>(StringComparer.Ordinal),
                entropy,
                utcNow)
            {
                TenantContext = tenantContext,
                Channel = channel,
            };
            var decision = action.Decide(input);
            observation.Decided(decision.Status, decision.RejectionReason);
            var effectPlan = effectPipeline.Plan(decision, descriptor.RequiredCapabilities);
            await effectPipeline.ApplyAsync(
                commandId,
                descriptor.GameId,
                aggregateId,
                command,
                state,
                decision,
                effectPlan,
                executionContext,
                session,
                tenantContext,
                ct);
            if (executionHistoryCollector is not null)
            {
                await executionHistoryCollector.AppendAsync(
                    commandId,
                    descriptor.GameId,
                    aggregateId,
                    command,
                    state,
                    decision,
                    entropy,
                    utcNow,
                    session,
                    ct);
            }
            await inbox.CompleteAsync(commandId, decision.Result, session, ct);
            observation.Committing();
            await session.CommitAsync(ct);
            committed = true;
            observation.Committed();
            return decision.Result;
        }
        catch (Exception exception)
        {
            if (session is not null && !committed)
            {
                try
                {
                    await session.RollbackAsync(CancellationToken.None);
                    observation.RolledBack();
                }
                catch (Exception rollbackException)
                {
                    exception.Data["RollbackException"] = rollbackException;
                }
            }
            observation.Failed(exception);
            throw;
        }
        finally
        {
            if (transactionStartedAt != 0)
                observation.TransactionFinished(Stopwatch.GetElapsedTime(transactionStartedAt));
            if (session is not null)
                await session.DisposeAsync();
        }
    }

    private IEnumerable<string> BuildLockKeys(
        TCommand command,
        string commandId,
        string aggregateId,
        TenantContext? tenantContext)
    {
        yield return $"command:{commandId}";
        if (tenantContext is { } tenant)
            yield return $"tenant-game:{tenant.TenantId.Value}:{tenant.ScopeId.Value}:{descriptor.GameId}:{aggregateId}";
        else
            yield return $"game:{descriptor.GameId}:{aggregateId}";
        foreach (var lockKey in descriptor.AdditionalLockKeys(command))
            yield return lockKey;
    }

    private static EntropyValue CreateEntropy(IReadOnlyList<string> names)
    {
        var values = new Dictionary<string, double>(StringComparer.Ordinal);
        Span<byte> bytes = stackalloc byte[sizeof(ulong)];
        foreach (var name in names)
        {
            RandomNumberGenerator.Fill(bytes);
            var raw = BinaryPrimitives.ReadUInt64LittleEndian(bytes);
            var value = (raw >> 11) * (1.0 / (1UL << 53));
            if (!values.TryAdd(name, value))
                throw new InvalidOperationException($"Duplicate entropy name '{name}'.");
        }
        return new EntropyValue(values);
    }
}

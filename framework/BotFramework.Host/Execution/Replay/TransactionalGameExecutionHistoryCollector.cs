using System.Text.Json;
using BotFramework.Sdk.Events.Contracts;
using BotFramework.Sdk.Execution;
using Dapper;

namespace BotFramework.Host.Execution;

/// <summary>
/// Captures the complete input, state transition, entropy and declared effects
/// of an atomic game command in its existing database transaction.
/// </summary>
internal sealed class TransactionalGameExecutionHistoryCollector
    : ITransactionalGameExecutionHistoryCollector
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task AppendAsync<TCommand, TState, TResult>(
        string commandId,
        string gameId,
        string aggregateId,
        TCommand command,
        TState currentState,
        GameDecision<TState, TResult> decision,
        EntropyValue entropy,
        DateTimeOffset occurredAt,
        IGameExecutionSession session,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);
        ArgumentException.ThrowIfNullOrWhiteSpace(aggregateId);
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(entropy);
        ArgumentNullException.ThrowIfNull(session);

        var commandPayload = Payload(command, typeof(TCommand));
        var stateType = TypeName(typeof(TState));
        var previousState = Payload(currentState, typeof(TState));
        var nextState = Payload(decision.NewState, typeof(TState));
        var resultPayload = Payload(decision.Result, typeof(TResult));
        var effects = Effects(decision.EffectSet);
        const string sql = """
            INSERT INTO game_execution_history (
                command_id,
                game_id,
                aggregate_id,
                command_type,
                command_payload,
                state_type,
                previous_state,
                next_state,
                result_type,
                result_payload,
                decision_status,
                rejection_reason,
                entropy,
                effects,
                occurred_at)
            VALUES (
                @commandId,
                @gameId,
                @aggregateId,
                @commandType,
                CAST(@commandPayload AS jsonb),
                @stateType,
                CAST(@previousState AS jsonb),
                CAST(@nextState AS jsonb),
                @resultType,
                CAST(@resultPayload AS jsonb),
                @decisionStatus,
                @rejectionReason,
                CAST(@entropy AS jsonb),
                CAST(@effects AS jsonb),
                @occurredAt)
            ON CONFLICT (command_id) DO NOTHING
            """;
        await session.Connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                commandId,
                gameId,
                aggregateId,
                commandType = commandPayload.TypeName,
                commandPayload = commandPayload.Json,
                stateType,
                previousState = previousState.Json,
                nextState = nextState.Json,
                resultType = resultPayload.TypeName,
                resultPayload = resultPayload.Json,
                decisionStatus = decision.Status.ToString().ToLowerInvariant(),
                decision.RejectionReason,
                entropy = JsonSerializer.Serialize(entropy.Values, JsonOptions),
                effects = JsonSerializer.Serialize(effects, JsonOptions),
                occurredAt,
            },
            session.Transaction,
            cancellationToken: ct));
    }

    private static GameReplayPayload Payload<T>(T value, Type declaredType)
    {
        ArgumentNullException.ThrowIfNull(declaredType);
        var type = value is null ? declaredType : value.GetType();
        return new(TypeName(type), JsonSerializer.Serialize(value, type, JsonOptions));
    }

    private static IReadOnlyList<GameReplayEffect> Effects(GameEffectSet effects)
    {
        ArgumentNullException.ThrowIfNull(effects);
        var result = new List<GameReplayEffect>();
        Append(result, "economy", effects.Economy);
        Append(result, "quota", effects.Quotas);
        Append(result, "record", effects.Records);
        Append(result, "custom", effects.Custom);
        Append(result, "event", effects.Events);
        Append(result, "schedule", effects.Schedules);
        return result;
    }

    private static void Append<T>(
        List<GameReplayEffect> output,
        string category,
        IReadOnlyList<T> values)
    {
        for (var index = 0; index < values.Count; index++)
        {
            var value = values[index];
            if (value is null)
                throw new InvalidOperationException("Replay effects cannot contain null.");
            output.Add(new(category, index, Payload(value, typeof(T))));
        }
    }

    private static string TypeName(Type type) => type.AssemblyQualifiedName
        ?? throw new InvalidOperationException($"Type '{type.Name}' has no assembly-qualified name.");
}

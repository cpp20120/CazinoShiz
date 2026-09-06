using System.Text.Json;
using Dapper;
using BotFramework.Sdk.Execution;

namespace BotFramework.Host.Execution;

internal sealed class PostgresGameEffectOutbox(INpgsqlConnectionFactory connections)
    : ITransactionalGameEffectOutbox
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task AppendAsync(
        string commandId,
        string gameId,
        string aggregateId,
        IReadOnlyList<(int EffectIndex, IDurableGameEffect Effect)> effects,
        IGameExecutionContext context,
        IGameExecutionSession session,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(effects);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(session);
        if (effects.Count == 0)
            return;

        var tenant = context.TenantContext;
        foreach (var (effectIndex, effect) in effects)
        {
            var type = effect.GetType();
            var payload = JsonSerializer.Serialize(effect, type, JsonOptions);
            await session.Connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO game_effect_outbox (
                    command_id, effect_index, game_id, aggregate_id, type_name, payload,
                    operation_id, tenant_id, scope_id, player_id, request_id, correlation_id,
                    channel, channel_container_id, channel_topic_id)
                VALUES (
                    @commandId, @effectIndex, @gameId, @aggregateId, @typeName, CAST(@payload AS jsonb),
                    @operationId, @tenantId, @scopeId, @playerId, @requestId, @correlationId,
                    @channel, @channelContainerId, @channelTopicId)
                ON CONFLICT (command_id, effect_index) DO NOTHING
                """,
                new
                {
                    commandId,
                    effectIndex,
                    gameId,
                    aggregateId,
                    typeName = type.AssemblyQualifiedName
                        ?? throw new InvalidOperationException($"Effect type '{type}' has no assembly-qualified name."),
                    payload,
                    operationId = context.OperationId,
                    tenantId = tenant?.TenantId.Value,
                    scopeId = tenant?.ScopeId.Value,
                    playerId = tenant?.PlayerId?.Value,
                    requestId = tenant?.RequestId.Value,
                    correlationId = tenant?.CorrelationId.Value,
                    channel = tenant?.Channel.ToString(),
                    channelContainerId = tenant?.ChannelContainerId,
                    channelTopicId = tenant?.ChannelTopicId,
                },
                session.Transaction,
                cancellationToken: ct));
        }
    }

    public async Task<IReadOnlyList<GameEffectOutboxItem>> ClaimAsync(
        int limit,
        TimeSpan lease,
        CancellationToken ct)
    {
        await using var connection = await connections.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        var rows = await connection.QueryAsync<GameEffectOutboxItem>(new CommandDefinition(
            """
            WITH due AS (
                SELECT candidate.id
                FROM game_effect_outbox AS candidate
                WHERE ((candidate.status = 'pending' AND candidate.next_attempt_at <= now())
                    OR (candidate.status = 'sending' AND candidate.locked_until <= now()))
                  AND NOT EXISTS (
                      SELECT 1
                      FROM game_effect_outbox AS earlier
                      WHERE earlier.command_id = candidate.command_id
                        AND earlier.effect_index < candidate.effect_index
                        AND earlier.status <> 'sent')
                ORDER BY candidate.next_attempt_at, candidate.id
                LIMIT @limit
                FOR UPDATE OF candidate SKIP LOCKED
            )
            UPDATE game_effect_outbox AS outbox
            SET status = 'sending',
                attempts = outbox.attempts + 1,
                locked_until = now() + (@leaseMs * interval '1 millisecond')
            FROM due
            WHERE outbox.id = due.id
            RETURNING outbox.id AS Id,
                      outbox.command_id AS CommandId,
                      outbox.game_id AS GameId,
                      outbox.aggregate_id AS AggregateId,
                      outbox.type_name AS TypeName,
                      outbox.payload::text AS Payload,
                      outbox.operation_id AS OperationId,
                      outbox.tenant_id AS TenantId,
                      outbox.scope_id AS ScopeId,
                      outbox.player_id AS PlayerId,
                      outbox.request_id AS RequestId,
                      outbox.correlation_id AS CorrelationId,
                      outbox.channel AS Channel,
                      outbox.channel_container_id AS ChannelContainerId,
                      outbox.channel_topic_id AS ChannelTopicId,
                      outbox.attempts AS Attempts
            """,
            new
            {
                limit = Math.Clamp(limit, 1, 100),
                leaseMs = Math.Max(1, lease.TotalMilliseconds),
            },
            transaction,
            cancellationToken: ct));
        await transaction.CommitAsync(ct);
        return rows.ToArray();
    }

    public async Task MarkSentAsync(long id, CancellationToken ct)
    {
        await using var connection = await connections.OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE game_effect_outbox
            SET status = 'sent', sent_at = now(), locked_until = NULL, last_error = NULL
            WHERE id = @id
            """,
            new { id },
            cancellationToken: ct));
    }

    public async Task MarkFailedAsync(long id, string error, int attempts, CancellationToken ct)
    {
        var retrySeconds = Math.Min(300, Math.Pow(2, Math.Min(attempts, 8)));
        await using var connection = await connections.OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE game_effect_outbox
            SET status = 'pending',
                next_attempt_at = now() + (@retrySeconds * interval '1 second'),
                locked_until = NULL,
                last_error = @error
            WHERE id = @id
            """,
            new
            {
                id,
                retrySeconds,
                error = error.Length <= 4000 ? error : error[..4000],
            },
            cancellationToken: ct));
    }
}

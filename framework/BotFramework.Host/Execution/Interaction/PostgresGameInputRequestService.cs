using System.Text.Json;
using Dapper;
using BotFramework.Sdk.Execution;

namespace BotFramework.Host.Execution;

internal sealed class PostgresGameInputRequestService(
    INpgsqlConnectionFactory connections,
    TimeProvider timeProvider) : IGameInputRequestService, ITransactionalGameInputRequestStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task CreateAsync(
        InputRequestEffect effect,
        IGameExecutionContext context,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(effect);
        ArgumentNullException.ThrowIfNull(context);
        var gameId = GameInputRequestData.RequireId(
            context.GameId ?? string.Empty,
            nameof(context.GameId));
        var aggregateId = GameInputRequestData.RequireId(
            context.AggregateId ?? string.Empty,
            nameof(context.AggregateId));

        await context.ExecuteAsync(
            """
            INSERT INTO game_input_requests (
                request_id, game_id, aggregate_id, expected_player_id, scope_id,
                route, payload, allowed_values, session_id, expires_at,
                created_command_id)
            VALUES (
                @requestId, @gameId, @aggregateId, @expectedPlayerId, @scopeId,
                @route, CAST(@payload AS jsonb), CAST(@allowedValues AS jsonb), @sessionId, @expiresAt,
                @commandId)
            ON CONFLICT (request_id) DO NOTHING
            """,
            new
            {
                requestId = effect.RequestId,
                gameId,
                aggregateId,
                expectedPlayerId = effect.ExpectedPlayerId,
                scopeId = effect.ScopeId,
                route = effect.Route,
                payload = effect.Payload,
                allowedValues = JsonSerializer.Serialize(effect.AllowedValues, JsonOptions),
                sessionId = effect.SessionId,
                expiresAt = effect.ExpiresAt,
                commandId = context.OperationId,
            },
            ct);
    }

    public async Task<GameInputRequest?> GetAsync(string requestId, CancellationToken ct)
    {
        requestId = GameInputRequestData.RequireId(requestId, nameof(requestId));
        await using var connection = await connections.OpenAsync(ct);
        var row = await ReadAsync(connection, transaction: null, requestId, ct);
        return row?.ToRequest(timeProvider.GetUtcNow());
    }

    public async Task<GameInputRequestConsumeResult> ConsumeAsync(
        GameInputSubmission submission,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(submission);
        await using var connection = await connections.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        var consumed = await connection.QuerySingleOrDefaultAsync<Row>(new CommandDefinition(
            """
            UPDATE game_input_requests
            SET status = 'consumed',
                consumed_at = now(),
                consumed_correlation_id = @correlationId,
                consumed_value = @value,
                updated_at = now()
            WHERE request_id = @requestId
              AND expected_player_id = @playerId
              AND scope_id = @scopeId
              AND status = 'pending'
              AND expires_at > now()
              AND (jsonb_array_length(allowed_values) = 0 OR allowed_values @> jsonb_build_array(@value))
            RETURNING request_id AS RequestId,
                      game_id AS GameId,
                      aggregate_id AS AggregateId,
                      expected_player_id AS ExpectedPlayerId,
                      scope_id AS ScopeId,
                      route AS Route,
                      payload::text AS Payload,
                      allowed_values::text AS AllowedValues,
                      session_id AS SessionId,
                      expires_at AS ExpiresAt,
                      status AS Status,
                      consumed_correlation_id AS ConsumedCorrelationId,
                      consumed_value AS ConsumedValue,
                      created_at AS CreatedAt,
                      consumed_at AS ConsumedAt
            """,
            new
            {
                requestId = submission.RequestId,
                playerId = submission.PlayerId,
                scopeId = submission.ScopeId,
                value = submission.Value,
                correlationId = submission.CorrelationId,
            },
            transaction,
            cancellationToken: ct));
        if (consumed is not null)
        {
            await transaction.CommitAsync(ct);
            return new(GameInputRequestConsumeStatus.Accepted, consumed.ToRequest(timeProvider.GetUtcNow()));
        }

        var row = await ReadAsync(connection, transaction, submission.RequestId, ct);
        if (row is null)
        {
            await transaction.CommitAsync(ct);
            return new(GameInputRequestConsumeStatus.NotFound, null);
        }

        var request = row.ToRequest(timeProvider.GetUtcNow());
        var status = DetermineStatus(request, submission);
        if (status == GameInputRequestConsumeStatus.Expired && request.Status == GameInputRequestStatus.Pending)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE game_input_requests
                SET status = 'expired', updated_at = now()
                WHERE request_id = @requestId AND status = 'pending'
                """,
                new { requestId = request.RequestId },
                transaction,
                cancellationToken: ct));
            request = request with { Status = GameInputRequestStatus.Expired };
        }

        await transaction.CommitAsync(ct);
        return new(status, request);
    }

    private static GameInputRequestConsumeStatus DetermineStatus(
        GameInputRequest request,
        GameInputSubmission submission)
    {
        if (!string.Equals(request.ExpectedPlayerId, submission.PlayerId, StringComparison.Ordinal)
            || !string.Equals(request.ScopeId, submission.ScopeId, StringComparison.Ordinal))
        {
            return GameInputRequestConsumeStatus.Forbidden;
        }

        if (request.Status == GameInputRequestStatus.Expired)
            return GameInputRequestConsumeStatus.Expired;
        if (request.Status == GameInputRequestStatus.Cancelled)
            return GameInputRequestConsumeStatus.Cancelled;
        if (request.Status == GameInputRequestStatus.Consumed)
        {
            return string.Equals(request.ConsumedCorrelationId, submission.CorrelationId, StringComparison.Ordinal)
                ? GameInputRequestConsumeStatus.AlreadyConsumed
                : GameInputRequestConsumeStatus.ConsumedByAnotherCorrelation;
        }

        return request.AllowedValues.Count != 0 && !request.AllowedValues.Contains(submission.Value, StringComparer.Ordinal)
            ? GameInputRequestConsumeStatus.InvalidValue
            : GameInputRequestConsumeStatus.Expired;
    }

    private static async Task<Row?> ReadAsync(
        System.Data.Common.DbConnection connection,
        System.Data.IDbTransaction? transaction,
        string requestId,
        CancellationToken ct) =>
        await connection.QuerySingleOrDefaultAsync<Row>(new CommandDefinition(
            """
            SELECT request_id AS RequestId,
                   game_id AS GameId,
                   aggregate_id AS AggregateId,
                   expected_player_id AS ExpectedPlayerId,
                   scope_id AS ScopeId,
                   route AS Route,
                   payload::text AS Payload,
                   allowed_values::text AS AllowedValues,
                   session_id AS SessionId,
                   expires_at AS ExpiresAt,
                   status AS Status,
                   consumed_correlation_id AS ConsumedCorrelationId,
                   consumed_value AS ConsumedValue,
                   created_at AS CreatedAt,
                   consumed_at AS ConsumedAt
            FROM game_input_requests
            WHERE request_id = @requestId
            FOR UPDATE
            """,
            new { requestId },
            transaction,
            cancellationToken: ct));

    private sealed class Row
    {
        public required string RequestId { get; init; }
        public required string GameId { get; init; }
        public required string AggregateId { get; init; }
        public required string ExpectedPlayerId { get; init; }
        public required string ScopeId { get; init; }
        public required string Route { get; init; }
        public required string Payload { get; init; }
        public required string AllowedValues { get; init; }
        public string? SessionId { get; init; }
        public DateTimeOffset ExpiresAt { get; init; }
        public required string Status { get; init; }
        public string? ConsumedCorrelationId { get; init; }
        public string? ConsumedValue { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        public DateTimeOffset? ConsumedAt { get; init; }

        public GameInputRequest ToRequest(DateTimeOffset now) => new(
            RequestId,
            GameId,
            AggregateId,
            ExpectedPlayerId,
            ScopeId,
            Route,
            Payload,
            JsonSerializer.Deserialize<string[]>(AllowedValues, JsonOptions) ?? [],
            SessionId,
            ExpiresAt,
            string.Equals(Status, "pending", StringComparison.Ordinal) && ExpiresAt <= now
                ? GameInputRequestStatus.Expired
                : Enum.Parse<GameInputRequestStatus>(Status, ignoreCase: true),
            ConsumedCorrelationId,
            ConsumedValue,
            CreatedAt,
            ConsumedAt);
    }
}

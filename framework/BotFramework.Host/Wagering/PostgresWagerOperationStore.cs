using BotFramework.Contracts.Wagering;
using BotFramework.Contracts.Messaging;
using BotFramework.Host.Persistence.Connections;
using Dapper;

namespace BotFramework.Host.Wagering;

public sealed class PostgresWagerOperationStore(
    INpgsqlConnectionFactory connections,
    IIntegrationInboxContextAccessor inboxContext) : IWagerOperationStore
{
    public async Task<WagerOperation> CreateOrGetAsync(WagerOperation operation, CancellationToken ct)
    {
        if (inboxContext.Current is { } current)
        {
            await InsertAsync(current.Connection, current.Transaction, operation, ct);
            return await GetRequiredAsync(current.Connection, current.Transaction, operation.OperationId, ct);
        }

        await using var connection = await connections.OpenAsync(ct);
        await InsertAsync(connection, null, operation, ct);
        return await GetRequiredAsync(connection, null, operation.OperationId, ct);
    }

    public async Task<WagerOperation?> GetAsync(string operationId, CancellationToken ct)
    {
        if (inboxContext.Current is { } current)
            return await QueryAsync(current.Connection, current.Transaction, operationId, ct);

        await using var connection = await connections.OpenAsync(ct);
        return await QueryAsync(connection, null, operationId, ct);
    }

    public async Task<WagerOperation?> GetByBetIdAsync(string betId, CancellationToken ct)
    {
        if (inboxContext.Current is { } current)
            return await QueryByBetAsync(current.Connection, current.Transaction, betId, ct);

        await using var connection = await connections.OpenAsync(ct);
        return await QueryByBetAsync(connection, null, betId, ct);
    }

    public async Task<WagerOperation> TransitionAsync(string operationId, WagerOperationStatus status, string? outcomeCode, string? errorCode, CancellationToken ct)
    {
        if (inboxContext.Current is { } current)
        {
            await UpdateAsync(current.Connection, current.Transaction, operationId, status, outcomeCode, errorCode, ct);
            return await GetRequiredAsync(current.Connection, current.Transaction, operationId, ct);
        }

        await using var connection = await connections.OpenAsync(ct);
        await UpdateAsync(connection, null, operationId, status, outcomeCode, errorCode, ct);
        return await GetRequiredAsync(connection, null, operationId, ct);
    }

    public async Task<WagerOperation?> TryTransitionAsync(
        string operationId,
        WagerOperationStatus expectedStatus,
        WagerOperationStatus status,
        string? outcomeCode,
        string? errorCode,
        CancellationToken ct,
        long? payout = null)
    {
        if (inboxContext.Current is { } current)
            return await TryUpdateAsync(current.Connection, current.Transaction, operationId, expectedStatus, status, outcomeCode, errorCode, payout, ct);

        await using var connection = await connections.OpenAsync(ct);
        return await TryUpdateAsync(connection, null, operationId, expectedStatus, status, outcomeCode, errorCode, payout, ct);
    }

    private static async Task InsertAsync(
        System.Data.Common.DbConnection connection,
        System.Data.Common.DbTransaction? transaction,
        WagerOperation operation,
        CancellationToken ct) =>
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO wager_operations (operation_id, bet_id, game_id, player_id, game_input, terms, status, created_at, updated_at)
            VALUES (@OperationId, @BetId, @GameId, @PlayerId, CAST(@GameInput AS jsonb), CAST(@TermsJson AS jsonb), @Status, @CreatedAt, @UpdatedAt)
            ON CONFLICT (operation_id) DO NOTHING
            """, operation, transaction, cancellationToken: ct));

    private static async Task UpdateAsync(
        System.Data.Common.DbConnection connection,
        System.Data.Common.DbTransaction? transaction,
        string operationId,
        WagerOperationStatus status,
        string? outcomeCode,
        string? errorCode,
        CancellationToken ct) =>
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE wager_operations SET status = @status, outcome_code = @outcomeCode,
                error_code = @errorCode, updated_at = now()
            WHERE operation_id = @operationId
            """, new { operationId, status, outcomeCode, errorCode }, transaction, cancellationToken: ct));

    private static async Task<WagerOperation?> TryUpdateAsync(
        System.Data.Common.DbConnection connection,
        System.Data.Common.DbTransaction? transaction,
        string operationId,
        WagerOperationStatus expectedStatus,
        WagerOperationStatus status,
        string? outcomeCode,
        string? errorCode,
        long? payout,
        CancellationToken ct)
    {
        var affected = await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE wager_operations SET status = @status, outcome_code = @outcomeCode,
                error_code = @errorCode, payout = COALESCE(@payout, payout), updated_at = now()
            WHERE operation_id = @operationId AND status = @expectedStatus
            """, new { operationId, expectedStatus, status, outcomeCode, errorCode, payout }, transaction, cancellationToken: ct));
        return affected == 1
            ? await GetRequiredAsync(connection, transaction, operationId, ct)
            : null;
    }

    private static Task<WagerOperation?> QueryAsync(
        System.Data.Common.DbConnection connection,
        System.Data.Common.DbTransaction? transaction,
        string operationId,
        CancellationToken ct) =>
        connection.QuerySingleOrDefaultAsync<WagerOperation>(new CommandDefinition(
            Select + " WHERE operation_id = @operationId",
            new { operationId },
            transaction,
            cancellationToken: ct));

    private static Task<WagerOperation?> QueryByBetAsync(
        System.Data.Common.DbConnection connection,
        System.Data.Common.DbTransaction? transaction,
        string betId,
        CancellationToken ct) =>
        connection.QuerySingleOrDefaultAsync<WagerOperation>(new CommandDefinition(
            Select + " WHERE bet_id = @betId",
            new { betId },
            transaction,
            cancellationToken: ct));

    private static async Task<WagerOperation> GetRequiredAsync(
        System.Data.Common.DbConnection connection,
        System.Data.Common.DbTransaction? transaction,
        string operationId,
        CancellationToken ct) =>
        await connection.QuerySingleAsync<WagerOperation>(new CommandDefinition(
            Select + " WHERE operation_id = @operationId",
            new { operationId },
            transaction,
            cancellationToken: ct));

    private const string Select = "SELECT operation_id AS OperationId, bet_id AS BetId, game_id AS GameId, player_id AS PlayerId, game_input::text AS GameInput, terms::text AS TermsJson, status AS Status, outcome_code AS OutcomeCode, error_code AS ErrorCode, created_at AS CreatedAt, updated_at AS UpdatedAt, payout AS Payout FROM wager_operations";
}

using System.Data;
using System.Data.Common;
using BotFramework.Contracts.Messaging;
using BotFramework.Contracts.Tenancy;
using BotFramework.Contracts.Wagering;
using BotFramework.Host.Execution;
using BotFramework.Host.Persistence.Connections;
using Dapper;

namespace BotFramework.Host.Wagering;

/// <summary>
/// In-process Ledger adapter used by the monolith while Wagering and Ledger
/// are still deployed together. The reservation table is the idempotency
/// boundary; wallet mutations and reservation state change share one DB
/// transaction. A separate Ledger service can consume the same contracts
/// without changing the game slice.
/// </summary>
public sealed class PostgresWagerLedgerCommandHandler(
    INpgsqlConnectionFactory connections,
    IIntegrationInboxContextAccessor inboxContext,
    ITenantContextAccessor tenantContext,
    IIntegrationEventPublisher events)
    : IIntegrationCommandHandler<LedgerReservationRequested>,
      IIntegrationCommandHandler<LedgerSettlementRequested>,
      IIntegrationCommandHandler<LedgerReservationRefundRequested>
{
    public async Task HandleAsync(LedgerReservationRequested command, CancellationToken ct)
    {
        var tenant = RequireTenant();
        var result = await InTransactionAsync(
            (connection, transaction) => ReserveAsync(connection, transaction, tenant, command, ct),
            ct);

        await events.PublishAsync(
            new LedgerReservationCompleted(
                command.OperationId,
                command.BetId,
                command.PlayerId,
                result.Reserved,
                result.RejectionCode,
                command.OccurredAt),
            ct);
    }

    public async Task HandleAsync(LedgerSettlementRequested command, CancellationToken ct)
    {
        var tenant = RequireTenant();
        var result = await InTransactionAsync(
            (connection, transaction) => SettleAsync(connection, transaction, tenant, command, ct),
            ct);

        await events.PublishAsync(
            new LedgerSettlementCompleted(
                command.OperationId,
                command.BetId,
                command.PlayerId,
                result.Settled,
                result.ErrorCode,
                command.OccurredAt),
            ct);
    }

    public async Task HandleAsync(LedgerReservationRefundRequested command, CancellationToken ct)
    {
        var tenant = RequireTenant();
        var result = await InTransactionAsync(
            (connection, transaction) => RefundAsync(connection, transaction, tenant, command, ct),
            ct);

        await events.PublishAsync(
            new LedgerReservationRefundCompleted(
                command.WorkflowId,
                command.OperationId,
                command.BetId,
                command.PlayerId,
                result.Refunded,
                result.ErrorCode,
                command.OccurredAt),
            ct);
    }

    private async Task<ReservationResult> ReserveAsync(
        DbConnection connection,
        DbTransaction? transaction,
        TenantContext tenant,
        LedgerReservationRequested command,
        CancellationToken ct)
    {
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO wager_reservations (operation_id, bet_id, player_id, amount, currency, status)
            VALUES (@OperationId, @BetId, @PlayerId, @Amount, @Currency, 'processing')
            ON CONFLICT (operation_id) DO NOTHING
            """,
            command,
            transaction,
            cancellationToken: ct));

        var reservation = await connection.QuerySingleOrDefaultAsync<ReservationRow>(new CommandDefinition(
            """
            SELECT operation_id AS OperationId, bet_id AS BetId, player_id AS PlayerId,
                   amount AS Amount, currency AS Currency, status AS Status
            FROM wager_reservations
            WHERE operation_id = @OperationId
            FOR UPDATE
            """,
            new { command.OperationId },
            transaction,
            cancellationToken: ct));
        if (reservation is null)
            throw new InvalidOperationException($"Wager reservation '{command.OperationId}' was not created.");

        if (!string.Equals(reservation.BetId, command.BetId, StringComparison.Ordinal)
            || !string.Equals(reservation.PlayerId, command.PlayerId, StringComparison.Ordinal)
            || reservation.Amount != command.Amount
            || !string.Equals(reservation.Currency, command.Currency, StringComparison.Ordinal))
            return new ReservationResult(false, "reservation_conflict");

        if (string.Equals(reservation.Status, "reserved", StringComparison.Ordinal)
            || string.Equals(reservation.Status, "settled", StringComparison.Ordinal))
            return new ReservationResult(true, null);
        if (string.Equals(reservation.Status, "rejected", StringComparison.Ordinal))
            return new ReservationResult(false, "insufficient_funds");

        await connection.ExecuteAsync(new CommandDefinition(
            TenantWalletSql.Ensure,
            new
            {
                tenantId = tenant.TenantId.Value,
                scopeId = tenant.ScopeId.Value,
                playerId = command.PlayerId,
            },
            transaction,
            cancellationToken: ct));

        var balance = await connection.QuerySingleOrDefaultAsync<long?>(new CommandDefinition(
            """
            UPDATE tenant_wallets w
            SET coins = w.coins - @Amount,
                version = w.version + 1,
                updated_at = now()
            WHERE w.tenant_key = (SELECT tenant_key FROM tenants WHERE tenant_id = @TenantId)
              AND w.scope_key = (
                  SELECT scope_key FROM tenant_scopes
                  WHERE tenant_key = (SELECT tenant_key FROM tenants WHERE tenant_id = @TenantId)
                    AND scope_id = @ScopeId)
              AND w.player_id = @PlayerId
              AND w.coins >= @Amount
            RETURNING w.coins
            """,
            new
            {
                TenantId = tenant.TenantId.Value,
                ScopeId = tenant.ScopeId.Value,
                PlayerId = command.PlayerId,
                command.Amount,
            },
            transaction,
            cancellationToken: ct));

        if (balance is null)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE wager_reservations SET status = 'rejected' WHERE operation_id = @OperationId",
                new { command.OperationId },
                transaction,
                cancellationToken: ct));
            return new ReservationResult(false, "insufficient_funds");
        }

        await InsertLedgerAsync(
            connection,
            transaction,
            tenant,
            command.PlayerId,
            -command.Amount,
            balance.Value,
            "wager.reserve",
            command.OperationId,
            ct);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE wager_reservations SET status = 'reserved' WHERE operation_id = @OperationId",
            new { command.OperationId },
            transaction,
            cancellationToken: ct));
        return new ReservationResult(true, null);
    }

    private async Task<SettlementResult> SettleAsync(
        DbConnection connection,
        DbTransaction? transaction,
        TenantContext tenant,
        LedgerSettlementRequested command,
        CancellationToken ct)
    {
        var reservation = await connection.QuerySingleOrDefaultAsync<ReservationRow>(new CommandDefinition(
            """
            SELECT operation_id AS OperationId, bet_id AS BetId, player_id AS PlayerId,
                   amount AS Amount, currency AS Currency, status AS Status
            FROM wager_reservations
            WHERE operation_id = @OperationId AND bet_id = @BetId
            FOR UPDATE
            """,
            new { command.OperationId, command.BetId },
            transaction,
            cancellationToken: ct));
        if (reservation is null)
            return new SettlementResult(false, "reservation_not_found");
        if (!string.Equals(reservation.PlayerId, command.PlayerId, StringComparison.Ordinal)
            || !string.Equals(reservation.Currency, command.Currency, StringComparison.Ordinal))
            return new SettlementResult(false, "reservation_conflict");
        if (string.Equals(reservation.Status, "settled", StringComparison.Ordinal))
            return new SettlementResult(true, null);
        if (!string.Equals(reservation.Status, "reserved", StringComparison.Ordinal))
            return new SettlementResult(false, "reservation_not_active");
        if (command.Payout < 0)
            return new SettlementResult(false, "invalid_payout");

        await connection.ExecuteAsync(new CommandDefinition(
            TenantWalletSql.Ensure,
            new
            {
                tenantId = tenant.TenantId.Value,
                scopeId = tenant.ScopeId.Value,
                playerId = command.PlayerId,
            },
            transaction,
            cancellationToken: ct));

        var balance = await connection.QuerySingleAsync<long>(new CommandDefinition(
            """
            UPDATE tenant_wallets w
            SET coins = w.coins + @Payout,
                version = w.version + CASE WHEN @Payout = 0 THEN 0 ELSE 1 END,
                updated_at = now()
            WHERE w.tenant_key = (SELECT tenant_key FROM tenants WHERE tenant_id = @TenantId)
              AND w.scope_key = (
                  SELECT scope_key FROM tenant_scopes
                  WHERE tenant_key = (SELECT tenant_key FROM tenants WHERE tenant_id = @TenantId)
                    AND scope_id = @ScopeId)
              AND w.player_id = @PlayerId
            RETURNING w.coins
            """,
            new
            {
                TenantId = tenant.TenantId.Value,
                ScopeId = tenant.ScopeId.Value,
                PlayerId = command.PlayerId,
                command.Payout,
            },
            transaction,
            cancellationToken: ct));

        await InsertLedgerAsync(
            connection,
            transaction,
            tenant,
            command.PlayerId,
            command.Payout,
            balance,
            "wager.settle",
            command.OperationId,
            ct);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE wager_reservations
            SET status = 'settled', settled_at = now()
            WHERE operation_id = @OperationId AND status = 'reserved'
            """,
            new { command.OperationId },
            transaction,
            cancellationToken: ct));
        return new SettlementResult(true, null);
    }

    private async Task<RefundResult> RefundAsync(
        DbConnection connection,
        DbTransaction? transaction,
        TenantContext tenant,
        LedgerReservationRefundRequested command,
        CancellationToken ct)
    {
        var reservation = await connection.QuerySingleOrDefaultAsync<ReservationRow>(new CommandDefinition(
            """
            SELECT operation_id AS OperationId, bet_id AS BetId, player_id AS PlayerId,
                   amount AS Amount, currency AS Currency, status AS Status
            FROM wager_reservations
            WHERE operation_id = @OperationId AND bet_id = @BetId
            FOR UPDATE
            """,
            new { command.OperationId, command.BetId },
            transaction,
            cancellationToken: ct));
        if (reservation is null)
            return new RefundResult(false, "reservation_not_found");
        if (!string.Equals(reservation.PlayerId, command.PlayerId, StringComparison.Ordinal)
            || !string.Equals(reservation.Currency, command.Currency, StringComparison.Ordinal)
            || reservation.Amount != command.Amount)
            return new RefundResult(false, "reservation_conflict");
        if (string.Equals(reservation.Status, "refunded", StringComparison.Ordinal))
            return new RefundResult(true, null);
        if (!string.Equals(reservation.Status, "reserved", StringComparison.Ordinal))
            return new RefundResult(false, "reservation_not_active");

        await connection.ExecuteAsync(new CommandDefinition(
            TenantWalletSql.Ensure,
            new
            {
                tenantId = tenant.TenantId.Value,
                scopeId = tenant.ScopeId.Value,
                playerId = command.PlayerId,
            },
            transaction,
            cancellationToken: ct));

        var balance = await connection.QuerySingleAsync<long>(new CommandDefinition(
            """
            UPDATE tenant_wallets w
            SET coins = w.coins + @Amount,
                version = w.version + 1,
                updated_at = now()
            WHERE w.tenant_key = (SELECT tenant_key FROM tenants WHERE tenant_id = @TenantId)
              AND w.scope_key = (
                  SELECT scope_key FROM tenant_scopes
                  WHERE tenant_key = (SELECT tenant_key FROM tenants WHERE tenant_id = @TenantId)
                    AND scope_id = @ScopeId)
              AND w.player_id = @PlayerId
            RETURNING w.coins
            """,
            new
            {
                TenantId = tenant.TenantId.Value,
                ScopeId = tenant.ScopeId.Value,
                PlayerId = command.PlayerId,
                command.Amount,
            },
            transaction,
            cancellationToken: ct));

        await InsertLedgerAsync(
            connection,
            transaction,
            tenant,
            command.PlayerId,
            command.Amount,
            balance,
            "wager.reserve.refund",
            $"wager-refund:{command.OperationId}",
            ct);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE wager_reservations SET status = 'refunded', settled_at = now() WHERE operation_id = @OperationId AND status = 'reserved'",
            new { command.OperationId },
            transaction,
            cancellationToken: ct));
        return new RefundResult(true, null);
    }

    private static Task<int> InsertLedgerAsync(
        DbConnection connection,
        DbTransaction? transaction,
        TenantContext tenant,
        string playerId,
        long delta,
        long balance,
        string reason,
        string operationId,
        CancellationToken ct) =>
        connection.ExecuteAsync(new CommandDefinition(
            TenantWalletSql.InsertLedger,
            new
            {
                tenantId = tenant.TenantId.Value,
                scopeId = tenant.ScopeId.Value,
                playerId,
                delta,
                balance,
                reason,
                operationId,
            },
            transaction,
            cancellationToken: ct));

    private async Task<TResult> InTransactionAsync<TResult>(
        Func<DbConnection, DbTransaction?, Task<TResult>> action,
        CancellationToken ct)
    {
        if (inboxContext.Current is { } current)
            return await action(current.Connection, current.Transaction);

        await using var connection = await connections.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try
        {
            var result = await action(connection, transaction);
            await transaction.CommitAsync(ct);
            return result;
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }

    private TenantContext RequireTenant() =>
        tenantContext.Current
        ?? RequestMetadataContext.TryGetCurrent()?.TenantContext
        ?? throw new InvalidOperationException("Wager Ledger requires tenant context.");

    private sealed record ReservationRow(
        string OperationId,
        string BetId,
        string PlayerId,
        long Amount,
        string Currency,
        string Status);

    private sealed record ReservationResult(bool Reserved, string? RejectionCode);

    private sealed record SettlementResult(bool Settled, string? ErrorCode);

    private sealed record RefundResult(bool Refunded, string? ErrorCode);
}

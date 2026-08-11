using System.Data;
using System.Data.Common;
using BotFramework.Contracts.Ledger;
using BotFramework.Contracts.Messaging;
using BotFramework.Contracts.Tenancy;
using BotFramework.Host.Execution;
using BotFramework.Host.Persistence.Connections;
using Dapper;

namespace BotFramework.Host.Ledger;

/// <summary>
/// Local Postgres implementation of the generic Ledger primitives.
///
/// The framework owns orchestration and idempotency boundaries; this adapter
/// owns the wallet mutation. A remote deployment can replace these handlers
/// with a Ledger service consuming the same contracts.
/// </summary>
public sealed class PostgresLedgerOperationCommandHandler(
    INpgsqlConnectionFactory connections,
    IIntegrationInboxContextAccessor inboxContext,
    ITenantContextAccessor tenantContext,
    IIntegrationEventPublisher events)
    : IIntegrationCommandHandler<LedgerHoldRequested>,
      IIntegrationCommandHandler<LedgerCaptureRequested>,
      IIntegrationCommandHandler<LedgerReleaseRequested>,
      IIntegrationCommandHandler<LedgerRefundRequested>,
      IIntegrationCommandHandler<LedgerTransferRequested>,
      IIntegrationCommandHandler<LedgerAdjustmentRequested>
{
    public async Task HandleAsync(LedgerHoldRequested command, CancellationToken ct)
    {
        var tenant = RequireTenant();
        var result = await InTransactionAsync(
            (connection, transaction) => HoldAsync(connection, transaction, tenant, command, ct), ct);
        await PublishAsync(result, command.OccurredAt, ct);
    }

    public async Task HandleAsync(LedgerCaptureRequested command, CancellationToken ct)
    {
        var tenant = RequireTenant();
        var result = await InTransactionAsync(
            (connection, transaction) => CaptureAsync(connection, transaction, tenant, command, ct), ct);
        await PublishAsync(result, command.OccurredAt, ct);
    }

    public async Task HandleAsync(LedgerReleaseRequested command, CancellationToken ct)
    {
        var tenant = RequireTenant();
        var result = await InTransactionAsync(
            (connection, transaction) => ReleaseAsync(connection, transaction, tenant, command, ct), ct);
        await PublishAsync(result, command.OccurredAt, ct);
    }

    public async Task HandleAsync(LedgerRefundRequested command, CancellationToken ct)
    {
        var tenant = RequireTenant();
        var result = await InTransactionAsync(
            (connection, transaction) => RefundAsync(connection, transaction, tenant, command, ct), ct);
        await PublishAsync(result, command.OccurredAt, ct);
    }

    public async Task HandleAsync(LedgerTransferRequested command, CancellationToken ct)
    {
        var tenant = RequireTenant();
        var result = await InTransactionAsync(
            (connection, transaction) => TransferAsync(connection, transaction, tenant, command, ct), ct);
        await PublishAsync(result, command.OccurredAt, ct);
    }

    public async Task HandleAsync(LedgerAdjustmentRequested command, CancellationToken ct)
    {
        var tenant = RequireTenant();
        var result = await InTransactionAsync(
            (connection, transaction) => AdjustmentAsync(connection, transaction, tenant, command, ct), ct);
        await PublishAsync(result, command.OccurredAt, ct);
    }

    private static async Task<LedgerResult> HoldAsync(
        DbConnection connection,
        DbTransaction transaction,
        TenantContext tenant,
        LedgerHoldRequested command,
        CancellationToken ct)
    {
        Validate(command.OperationId, command.AccountId, command.Amount, command.Currency, command.Reason);
        if (string.IsNullOrWhiteSpace(command.HoldId))
            return Reject(LedgerOperationKind.Hold, command, "hold_id_required", command.HoldId);
        if (command.ExpiresAt <= command.OccurredAt)
            return Reject(LedgerOperationKind.Hold, command, "hold_expiry_invalid", command.HoldId);

        var operation = await EnsureOperationAsync(
            connection, transaction, tenant, command.OperationId, LedgerOperationKind.Hold,
            command.AccountId, null, command.HoldId, command.Amount, command.Currency, command.Reason, null, ct);
        if (!operation.IsCompatible(LedgerOperationKind.Hold, command.AccountId, null, command.HoldId,
                command.Amount, command.Currency))
            return Reject(LedgerOperationKind.Hold, command, "operation_conflict", command.HoldId);
        if (!string.Equals(operation.Status, "processing", StringComparison.Ordinal))
            return FromOperation(operation, LedgerOperationKind.Hold, command.Currency, command.AccountId, command.HoldId);

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO ledger_holds
                (tenant_key, scope_key, hold_id, operation_id, account_id, amount, currency, expires_at, status)
            SELECT t.tenant_key, s.scope_key, @holdId, @operationId, @accountId, @amount, @currency, @expiresAt, 'processing'
            FROM tenants t
            JOIN tenant_scopes s ON s.tenant_key = t.tenant_key AND s.scope_id = @scopeId
            WHERE t.tenant_id = @tenantId
            ON CONFLICT (tenant_key, scope_key, hold_id) DO NOTHING
            """,
            new
            {
                tenantId = tenant.TenantId.Value,
                scopeId = tenant.ScopeId.Value,
                holdId = command.HoldId,
                operationId = command.OperationId,
                accountId = command.AccountId,
                command.Amount,
                command.Currency,
                command.ExpiresAt,
            },
            transaction,
            cancellationToken: ct));

        var hold = await LockHoldAsync(connection, transaction, tenant, command.HoldId, ct);
        if (hold is null)
            return Reject(LedgerOperationKind.Hold, command, "hold_not_created", command.HoldId);
        if (!hold.IsCompatible(command.OperationId, command.AccountId, command.Amount, command.Currency))
            return Reject(LedgerOperationKind.Hold, command, "hold_conflict", command.HoldId);
        if (!string.Equals(hold.Status, "processing", StringComparison.Ordinal))
            return FromHold(hold, LedgerOperationKind.Hold, command.Currency, command.AccountId, command.HoldId);

        await EnsureWalletAsync(connection, transaction, tenant, command.AccountId, ct);
        var balance = await ApplyWalletDeltaAsync(
            connection, transaction, tenant, command.AccountId, -command.Amount, ct);
        if (balance is null)
        {
            await CompleteOperationAsync(connection, transaction, tenant, command.OperationId,
                "rejected", 0, null, "insufficient_funds", ct);
            await UpdateHoldAsync(connection, transaction, tenant, command.HoldId,
                "rejected", 0, 0, 0, "insufficient_funds", ct);
            return Reject(LedgerOperationKind.Hold, command, "insufficient_funds", command.HoldId);
        }

        await InsertLedgerAsync(connection, transaction, tenant, command.AccountId, -command.Amount,
            balance.Value, command.Reason, command.OperationId, ct);
        await UpdateHoldAsync(connection, transaction, tenant, command.HoldId,
            "held", 0, 0, 0, null, ct);
        await CompleteOperationAsync(connection, transaction, tenant, command.OperationId,
            "held", command.Amount, command.Amount, null, ct);
        return Success(LedgerOperationKind.Hold, command, "held", command.AccountId, command.HoldId,
            command.Amount, command.Amount);
    }

    private static async Task<LedgerResult> CaptureAsync(
        DbConnection connection,
        DbTransaction transaction,
        TenantContext tenant,
        LedgerCaptureRequested command,
        CancellationToken ct)
    {
        Validate(command.OperationId, command.AccountId, command.Amount, command.Currency, command.Reason);
        var operation = await EnsureOperationAsync(
            connection, transaction, tenant, command.OperationId, LedgerOperationKind.Capture,
            command.AccountId, command.DestinationAccountId, command.HoldId, command.Amount, command.Currency, command.Reason, null, ct);
        if (!operation.IsCompatible(LedgerOperationKind.Capture, command.AccountId, command.DestinationAccountId, command.HoldId,
                command.Amount, command.Currency))
            return Reject(LedgerOperationKind.Capture, command, "operation_conflict", command.HoldId);
        if (!string.Equals(operation.Status, "processing", StringComparison.Ordinal))
            return FromOperation(operation, LedgerOperationKind.Capture, command.Currency, command.AccountId, command.HoldId);

        var hold = await LockHoldAsync(connection, transaction, tenant, command.HoldId, ct);
        if (hold is null)
            return Reject(LedgerOperationKind.Capture, command, "hold_not_found", command.HoldId);
        if (!hold.IsAccountAndCurrency(command.AccountId, command.Currency))
            return Reject(LedgerOperationKind.Capture, command, "hold_conflict", command.HoldId);
        if (hold.ExpiresAt <= command.OccurredAt)
            return Reject(LedgerOperationKind.Capture, command, "hold_expired", command.HoldId);
        var available = hold.AvailableAmount;
        if (available < command.Amount)
            return Reject(LedgerOperationKind.Capture, command, "hold_amount_exceeded", command.HoldId);

        var captured = hold.CapturedAmount + command.Amount;
        var status = captured == hold.Amount ? "captured" : "partially_captured";
        if (!string.IsNullOrWhiteSpace(command.DestinationAccountId))
        {
            await EnsureWalletAsync(connection, transaction, tenant, command.DestinationAccountId, ct);
            var destinationBalance = await ApplyWalletDeltaAsync(
                connection, transaction, tenant, command.DestinationAccountId, command.Amount, ct);
            if (destinationBalance is null)
                return Reject(LedgerOperationKind.Capture, command, "wallet_not_found", command.DestinationAccountId);
            await InsertLedgerAsync(connection, transaction, tenant, command.DestinationAccountId, command.Amount,
                destinationBalance.Value, command.Reason, $"{command.OperationId}:capture", ct);
        }
        await UpdateHoldAsync(connection, transaction, tenant, command.HoldId,
            status, captured, hold.ReleasedAmount, hold.RefundedAmount, null, ct);
        await CompleteOperationAsync(connection, transaction, tenant, command.OperationId,
            status, command.Amount, available - command.Amount, null, ct);
        return Success(LedgerOperationKind.Capture, command, status, command.AccountId, command.HoldId,
            command.Amount, available - command.Amount);
    }

    private static async Task<LedgerResult> ReleaseAsync(
        DbConnection connection,
        DbTransaction transaction,
        TenantContext tenant,
        LedgerReleaseRequested command,
        CancellationToken ct)
    {
        Validate(command.OperationId, command.AccountId, command.Amount, command.Currency, command.Reason);
        var operation = await EnsureOperationAsync(
            connection, transaction, tenant, command.OperationId, LedgerOperationKind.Release,
            command.AccountId, null, command.HoldId, command.Amount, command.Currency, command.Reason, null, ct);
        if (!operation.IsCompatible(LedgerOperationKind.Release, command.AccountId, null, command.HoldId,
                command.Amount, command.Currency))
            return Reject(LedgerOperationKind.Release, command, "operation_conflict", command.HoldId);
        if (!string.Equals(operation.Status, "processing", StringComparison.Ordinal))
            return FromOperation(operation, LedgerOperationKind.Release, command.Currency, command.AccountId, command.HoldId);

        var hold = await LockHoldAsync(connection, transaction, tenant, command.HoldId, ct);
        if (hold is null)
            return Reject(LedgerOperationKind.Release, command, "hold_not_found", command.HoldId);
        if (!hold.IsAccountAndCurrency(command.AccountId, command.Currency))
            return Reject(LedgerOperationKind.Release, command, "hold_conflict", command.HoldId);
        var available = hold.AvailableAmount;
        if (available < command.Amount)
            return Reject(LedgerOperationKind.Release, command, "hold_amount_exceeded", command.HoldId);

        await EnsureWalletAsync(connection, transaction, tenant, command.AccountId, ct);
        var balance = await ApplyWalletDeltaAsync(
            connection, transaction, tenant, command.AccountId, command.Amount, ct);
        if (balance is null)
            return Reject(LedgerOperationKind.Release, command, "wallet_not_found", command.HoldId);

        var released = hold.ReleasedAmount + command.Amount;
        var status = released == hold.Amount - hold.CapturedAmount
            ? "released"
            : "partially_released";
        await InsertLedgerAsync(connection, transaction, tenant, command.AccountId, command.Amount,
            balance.Value, command.Reason, command.OperationId, ct);
        await UpdateHoldAsync(connection, transaction, tenant, command.HoldId,
            status, hold.CapturedAmount, released, hold.RefundedAmount, null, ct);
        await CompleteOperationAsync(connection, transaction, tenant, command.OperationId,
            status, command.Amount, available - command.Amount, null, ct);
        return Success(LedgerOperationKind.Release, command, status, command.AccountId, command.HoldId,
            command.Amount, available - command.Amount);
    }

    private static async Task<LedgerResult> RefundAsync(
        DbConnection connection,
        DbTransaction transaction,
        TenantContext tenant,
        LedgerRefundRequested command,
        CancellationToken ct)
    {
        Validate(command.OperationId, command.AccountId, command.Amount, command.Currency, command.Reason);
        if (string.IsNullOrWhiteSpace(command.OriginalOperationId))
            return Reject(LedgerOperationKind.Refund, command, "original_operation_required", command.OriginalOperationId);

        var operation = await EnsureOperationAsync(
            connection, transaction, tenant, command.OperationId, LedgerOperationKind.Refund,
            command.AccountId, null, command.OriginalOperationId, command.Amount, command.Currency, command.Reason, null, ct);
        if (!operation.IsCompatible(LedgerOperationKind.Refund, command.AccountId, null,
                command.OriginalOperationId, command.Amount, command.Currency))
            return Reject(LedgerOperationKind.Refund, command, "operation_conflict", command.OriginalOperationId);
        if (!string.Equals(operation.Status, "processing", StringComparison.Ordinal))
            return FromOperation(operation, LedgerOperationKind.Refund, command.Currency, command.AccountId,
                command.OriginalOperationId);

        var hold = await LockHoldByReferenceAsync(
            connection, transaction, tenant, command.OriginalOperationId, ct);
        if (hold is null)
            return Reject(LedgerOperationKind.Refund, command, "original_operation_not_found", command.OriginalOperationId);
        if (!hold.IsAccountAndCurrency(command.AccountId, command.Currency))
            return Reject(LedgerOperationKind.Refund, command, "hold_conflict", command.OriginalOperationId);
        if (hold.CapturedAmount - hold.RefundedAmount < command.Amount)
            return Reject(LedgerOperationKind.Refund, command, "refund_amount_exceeded", command.OriginalOperationId);

        await EnsureWalletAsync(connection, transaction, tenant, command.AccountId, ct);
        var balance = await ApplyWalletDeltaAsync(
            connection, transaction, tenant, command.AccountId, command.Amount, ct);
        if (balance is null)
            return Reject(LedgerOperationKind.Refund, command, "wallet_not_found", command.OriginalOperationId);

        var refunded = hold.RefundedAmount + command.Amount;
        var status = refunded == hold.CapturedAmount ? "refunded" : "partially_refunded";
        await InsertLedgerAsync(connection, transaction, tenant, command.AccountId, command.Amount,
            balance.Value, command.Reason, command.OperationId, ct);
        await UpdateHoldAsync(connection, transaction, tenant, hold.HoldId,
            status, hold.CapturedAmount, hold.ReleasedAmount, refunded, null, ct);
        await CompleteOperationAsync(connection, transaction, tenant, command.OperationId,
            status, command.Amount, hold.CapturedAmount - refunded, null, ct);
        return Success(LedgerOperationKind.Refund, command, status, command.AccountId, hold.HoldId,
            command.Amount, hold.CapturedAmount - refunded);
    }

    private static async Task<LedgerResult> TransferAsync(
        DbConnection connection,
        DbTransaction transaction,
        TenantContext tenant,
        LedgerTransferRequested command,
        CancellationToken ct)
    {
        Validate(command.OperationId, command.FromAccountId, command.Amount, command.Currency, command.Reason);
        LedgerCommandValidation.ValidateAccount(command.ToAccountId, nameof(command.ToAccountId));
        if (string.Equals(command.FromAccountId, command.ToAccountId, StringComparison.Ordinal))
            return Reject(LedgerOperationKind.Transfer, command, "same_account", command.FromAccountId);

        var operation = await EnsureOperationAsync(
            connection, transaction, tenant, command.OperationId, LedgerOperationKind.Transfer,
            command.FromAccountId, command.ToAccountId, null, command.Amount, command.Currency, command.Reason, null, ct);
        if (!operation.IsCompatible(LedgerOperationKind.Transfer, command.FromAccountId, command.ToAccountId,
                null, command.Amount, command.Currency))
            return Reject(LedgerOperationKind.Transfer, command, "operation_conflict", command.FromAccountId);
        if (!string.Equals(operation.Status, "processing", StringComparison.Ordinal))
            return FromOperation(operation, LedgerOperationKind.Transfer, command.Currency, command.FromAccountId,
                command.ToAccountId);

        await EnsureWalletAsync(connection, transaction, tenant, command.FromAccountId, ct);
        await EnsureWalletAsync(connection, transaction, tenant, command.ToAccountId, ct);
        var accounts = (await connection.QueryAsync<WalletRow>(new CommandDefinition(
            """
            SELECT w.player_id AS AccountId, w.coins AS Balance
            FROM tenant_wallets w
            WHERE w.tenant_key = (SELECT tenant_key FROM tenants WHERE tenant_id = @tenantId)
              AND w.scope_key = (
                  SELECT scope_key FROM tenant_scopes
                  WHERE tenant_key = (SELECT tenant_key FROM tenants WHERE tenant_id = @tenantId)
                    AND scope_id = @scopeId)
              AND w.player_id IN @accountIds
            ORDER BY w.player_id
            FOR UPDATE
            """,
            new
            {
                tenantId = tenant.TenantId.Value,
                scopeId = tenant.ScopeId.Value,
                accountIds = new[] { command.FromAccountId, command.ToAccountId },
            },
            transaction,
            cancellationToken: ct))).ToDictionary(x => x.AccountId, StringComparer.Ordinal);
        if (!accounts.TryGetValue(command.FromAccountId, out var source)
            || !accounts.TryGetValue(command.ToAccountId, out var destination))
            return Reject(LedgerOperationKind.Transfer, command, "wallet_not_found", command.FromAccountId);
        if (source.Balance < command.Amount)
            return Reject(LedgerOperationKind.Transfer, command, "insufficient_funds", command.FromAccountId);

        await SetWalletBalanceAsync(connection, transaction, tenant, command.FromAccountId,
            source.Balance - command.Amount, ct);
        await SetWalletBalanceAsync(connection, transaction, tenant, command.ToAccountId,
            destination.Balance + command.Amount, ct);
        await InsertLedgerAsync(connection, transaction, tenant, command.FromAccountId, -command.Amount,
            source.Balance - command.Amount, command.Reason, $"{command.OperationId}:debit", ct);
        await InsertLedgerAsync(connection, transaction, tenant, command.ToAccountId, command.Amount,
            destination.Balance + command.Amount, command.Reason, $"{command.OperationId}:credit", ct);
        await CompleteOperationAsync(connection, transaction, tenant, command.OperationId,
            "completed", command.Amount, null, null, ct);
        return Success(LedgerOperationKind.Transfer, command, "completed", command.FromAccountId,
            command.ToAccountId, command.Amount, null);
    }

    private static async Task<LedgerResult> AdjustmentAsync(
        DbConnection connection,
        DbTransaction transaction,
        TenantContext tenant,
        LedgerAdjustmentRequested command,
        CancellationToken ct)
    {
        LedgerCommandValidation.ValidateCommon(command.OperationId, command.Currency, command.Reason);
        LedgerCommandValidation.ValidateAccount(command.AccountId);
        if (!string.Equals(command.Currency, "coins", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"The local wallet adapter does not support currency '{command.Currency}'.");
        if (command.Delta == 0)
            return Reject(LedgerOperationKind.Adjustment, command, "zero_delta", command.AccountId);
        if (string.IsNullOrWhiteSpace(command.ActorId))
            return Reject(LedgerOperationKind.Adjustment, command, "actor_required", command.AccountId);

        var operation = await EnsureOperationAsync(
            connection, transaction, tenant, command.OperationId, LedgerOperationKind.Adjustment,
            command.AccountId, null, null, Abs(command.Delta), command.Currency, command.Reason, command.ActorId, ct);
        if (!operation.IsCompatible(LedgerOperationKind.Adjustment, command.AccountId, null, null,
                Abs(command.Delta), command.Currency))
            return Reject(LedgerOperationKind.Adjustment, command, "operation_conflict", command.AccountId);
        if (!string.Equals(operation.Status, "processing", StringComparison.Ordinal))
            return FromOperation(operation, LedgerOperationKind.Adjustment, command.Currency, command.AccountId, null);

        await EnsureWalletAsync(connection, transaction, tenant, command.AccountId, ct);
        var balance = await ApplyWalletDeltaAsync(
            connection, transaction, tenant, command.AccountId, command.Delta, ct);
        if (balance is null)
            return Reject(LedgerOperationKind.Adjustment, command, "insufficient_funds", command.AccountId);

        await InsertLedgerAsync(connection, transaction, tenant, command.AccountId, command.Delta,
            balance.Value, $"{command.Reason}:actor={command.ActorId}", command.OperationId, ct);
        await CompleteOperationAsync(connection, transaction, tenant, command.OperationId,
            "completed", Abs(command.Delta), null, null, ct);
        return Success(LedgerOperationKind.Adjustment, command, "completed", command.AccountId, null,
            Abs(command.Delta), null);
    }

    private static async Task<LedgerOperationRow> EnsureOperationAsync(
        DbConnection connection,
        DbTransaction transaction,
        TenantContext tenant,
        string operationId,
        LedgerOperationKind kind,
        string? accountId,
        string? targetAccountId,
        string? referenceId,
        long amount,
        string currency,
        string reason,
        string? actorId,
        CancellationToken ct)
    {
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO ledger_operations
                (tenant_key, scope_key, operation_id, operation_kind, account_id,
                 target_account_id, reference_id, amount, currency, reason, actor_id, status)
            SELECT t.tenant_key, s.scope_key, @operationId, @operationKind, @accountId,
                   @targetAccountId, @referenceId, @amount, @currency, @reason, @actorId, 'processing'
            FROM tenants t
            JOIN tenant_scopes s ON s.tenant_key = t.tenant_key AND s.scope_id = @scopeId
            WHERE t.tenant_id = @tenantId
            ON CONFLICT (tenant_key, scope_key, operation_id) DO NOTHING
            """,
            new
            {
                tenantId = tenant.TenantId.Value,
                scopeId = tenant.ScopeId.Value,
                operationId,
                operationKind = kind.ToString().ToLowerInvariant(),
                accountId,
                targetAccountId,
                referenceId,
                amount,
                currency,
                reason,
                actorId,
            },
            transaction,
            cancellationToken: ct));

        return await connection.QuerySingleAsync<LedgerOperationRow>(new CommandDefinition(
            """
            SELECT operation_id AS OperationId, operation_kind AS OperationKind,
                   account_id AS AccountId, target_account_id AS TargetAccountId,
                   reference_id AS ReferenceId, amount AS Amount,
                   applied_amount AS AppliedAmount, remaining_amount AS RemainingAmount,
                   currency AS Currency, status AS Status, error_code AS ErrorCode
            FROM ledger_operations
            WHERE tenant_key = (SELECT tenant_key FROM tenants WHERE tenant_id = @tenantId)
              AND scope_key = (
                  SELECT scope_key FROM tenant_scopes
                  WHERE tenant_key = (SELECT tenant_key FROM tenants WHERE tenant_id = @tenantId)
                    AND scope_id = @scopeId)
              AND operation_id = @operationId
            FOR UPDATE
            """,
            new { tenantId = tenant.TenantId.Value, scopeId = tenant.ScopeId.Value, operationId },
            transaction,
            cancellationToken: ct));
    }

    private static async Task<LedgerHoldRow?> LockHoldAsync(
        DbConnection connection,
        DbTransaction transaction,
        TenantContext tenant,
        string holdId,
        CancellationToken ct) =>
        await connection.QuerySingleOrDefaultAsync<LedgerHoldRow>(new CommandDefinition(
            """
            SELECT hold_id AS HoldId, operation_id AS OperationId, account_id AS AccountId,
                   amount AS Amount, captured_amount AS CapturedAmount,
                   released_amount AS ReleasedAmount, refunded_amount AS RefundedAmount,
                   currency AS Currency, expires_at AS ExpiresAt, status AS Status
            FROM ledger_holds
            WHERE tenant_key = (SELECT tenant_key FROM tenants WHERE tenant_id = @tenantId)
              AND scope_key = (
                  SELECT scope_key FROM tenant_scopes
                  WHERE tenant_key = (SELECT tenant_key FROM tenants WHERE tenant_id = @tenantId)
                    AND scope_id = @scopeId)
              AND hold_id = @holdId
            FOR UPDATE
            """,
            new { tenantId = tenant.TenantId.Value, scopeId = tenant.ScopeId.Value, holdId },
            transaction,
            cancellationToken: ct));

    private static async Task<LedgerHoldRow?> LockHoldByReferenceAsync(
        DbConnection connection,
        DbTransaction transaction,
        TenantContext tenant,
        string operationId,
        CancellationToken ct) =>
        await connection.QuerySingleOrDefaultAsync<LedgerHoldRow>(new CommandDefinition(
            """
            SELECT hold_id AS HoldId, operation_id AS OperationId, account_id AS AccountId,
                   amount AS Amount, captured_amount AS CapturedAmount,
                   released_amount AS ReleasedAmount, refunded_amount AS RefundedAmount,
                   currency AS Currency, expires_at AS ExpiresAt, status AS Status
            FROM ledger_holds
            WHERE tenant_key = (SELECT tenant_key FROM tenants WHERE tenant_id = @tenantId)
              AND scope_key = (
                  SELECT scope_key FROM tenant_scopes
                  WHERE tenant_key = (SELECT tenant_key FROM tenants WHERE tenant_id = @tenantId)
                    AND scope_id = @scopeId)
              AND (
                  hold_id = @operationId
                  OR operation_id = @operationId
                  OR hold_id = (
                      SELECT reference_id
                      FROM ledger_operations
                      WHERE tenant_key = (SELECT tenant_key FROM tenants WHERE tenant_id = @tenantId)
                        AND scope_key = (
                            SELECT scope_key FROM tenant_scopes
                            WHERE tenant_key = (SELECT tenant_key FROM tenants WHERE tenant_id = @tenantId)
                              AND scope_id = @scopeId)
                        AND operation_id = @operationId
                        AND operation_kind = 'capture'))
            FOR UPDATE
            """,
            new { tenantId = tenant.TenantId.Value, scopeId = tenant.ScopeId.Value, operationId },
            transaction,
            cancellationToken: ct));

    private static Task<int> UpdateHoldAsync(
        DbConnection connection,
        DbTransaction transaction,
        TenantContext tenant,
        string holdId,
        string status,
        long capturedAmount,
        long releasedAmount,
        long refundedAmount,
        string? errorCode,
        CancellationToken ct) =>
        connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE ledger_holds
            SET status = @status, captured_amount = @capturedAmount,
                released_amount = @releasedAmount, refunded_amount = @refundedAmount,
                error_code = @errorCode, updated_at = now()
            WHERE tenant_key = (SELECT tenant_key FROM tenants WHERE tenant_id = @tenantId)
              AND scope_key = (
                  SELECT scope_key FROM tenant_scopes
                  WHERE tenant_key = (SELECT tenant_key FROM tenants WHERE tenant_id = @tenantId)
                    AND scope_id = @scopeId)
              AND hold_id = @holdId
            """,
            new
            {
                tenantId = tenant.TenantId.Value,
                scopeId = tenant.ScopeId.Value,
                holdId,
                status,
                capturedAmount,
                releasedAmount,
                refundedAmount,
                errorCode,
            },
            transaction,
            cancellationToken: ct));

    private static Task<int> CompleteOperationAsync(
        DbConnection connection,
        DbTransaction transaction,
        TenantContext tenant,
        string operationId,
        string status,
        long appliedAmount,
        long? remainingAmount,
        string? errorCode,
        CancellationToken ct) =>
        connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE ledger_operations
            SET status = @status, applied_amount = @appliedAmount,
                remaining_amount = @remainingAmount, error_code = @errorCode,
                updated_at = now()
            WHERE tenant_key = (SELECT tenant_key FROM tenants WHERE tenant_id = @tenantId)
              AND scope_key = (
                  SELECT scope_key FROM tenant_scopes
                  WHERE tenant_key = (SELECT tenant_key FROM tenants WHERE tenant_id = @tenantId)
                    AND scope_id = @scopeId)
              AND operation_id = @operationId
            """,
            new
            {
                tenantId = tenant.TenantId.Value,
                scopeId = tenant.ScopeId.Value,
                operationId,
                status,
                appliedAmount,
                remainingAmount,
                errorCode,
            },
            transaction,
            cancellationToken: ct));

    private static Task<int> EnsureWalletAsync(
        DbConnection connection,
        DbTransaction transaction,
        TenantContext tenant,
        string accountId,
        CancellationToken ct) =>
        connection.ExecuteAsync(new CommandDefinition(
            TenantWalletSql.Ensure,
            new
            {
                tenantId = tenant.TenantId.Value,
                scopeId = tenant.ScopeId.Value,
                playerId = accountId,
            },
            transaction,
            cancellationToken: ct));

    private static async Task<long?> ApplyWalletDeltaAsync(
        DbConnection connection,
        DbTransaction transaction,
        TenantContext tenant,
        string accountId,
        long delta,
        CancellationToken ct) =>
        await connection.QuerySingleOrDefaultAsync<long?>(new CommandDefinition(
            """
            UPDATE tenant_wallets w
            SET coins = w.coins + @delta, version = w.version + 1, updated_at = now()
            WHERE w.tenant_key = (SELECT tenant_key FROM tenants WHERE tenant_id = @tenantId)
              AND w.scope_key = (
                  SELECT scope_key FROM tenant_scopes
                  WHERE tenant_key = (SELECT tenant_key FROM tenants WHERE tenant_id = @tenantId)
                    AND scope_id = @scopeId)
              AND w.player_id = @accountId
              AND w.coins + @delta >= 0
            RETURNING w.coins
            """,
            new
            {
                tenantId = tenant.TenantId.Value,
                scopeId = tenant.ScopeId.Value,
                accountId,
                delta,
            },
            transaction,
            cancellationToken: ct));

    private static Task<int> SetWalletBalanceAsync(
        DbConnection connection,
        DbTransaction transaction,
        TenantContext tenant,
        string accountId,
        long balance,
        CancellationToken ct) =>
        connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE tenant_wallets w
            SET coins = @balance, version = w.version + 1, updated_at = now()
            WHERE w.tenant_key = (SELECT tenant_key FROM tenants WHERE tenant_id = @tenantId)
              AND w.scope_key = (
                  SELECT scope_key FROM tenant_scopes
                  WHERE tenant_key = (SELECT tenant_key FROM tenants WHERE tenant_id = @tenantId)
                    AND scope_id = @scopeId)
              AND w.player_id = @accountId
            """,
            new
            {
                tenantId = tenant.TenantId.Value,
                scopeId = tenant.ScopeId.Value,
                accountId,
                balance,
            },
            transaction,
            cancellationToken: ct));

    private static Task<int> InsertLedgerAsync(
        DbConnection connection,
        DbTransaction transaction,
        TenantContext tenant,
        string accountId,
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
                playerId = accountId,
                delta,
                balance,
                reason,
                operationId,
            },
            transaction,
            cancellationToken: ct));

    private async Task PublishAsync(LedgerResult result, DateTimeOffset occurredAt, CancellationToken ct) =>
        await events.PublishAsync(new LedgerOperationCompleted(
            result.OperationId,
            result.Kind,
            result.Status,
            result.Succeeded,
            result.AccountId,
            result.ReferenceId,
            result.AppliedAmount,
            result.RemainingAmount,
            result.Currency,
            result.ErrorCode,
            occurredAt), ct);

    private async Task<TResult> InTransactionAsync<TResult>(
        Func<DbConnection, DbTransaction, Task<TResult>> action,
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
        ?? throw new InvalidOperationException("Ledger requires tenant context.");

    private static void Validate(string operationId, string accountId, long amount, string currency, string reason)
    {
        LedgerCommandValidation.ValidateCommon(operationId, currency, reason);
        LedgerCommandValidation.ValidateAccount(accountId);
        LedgerCommandValidation.ValidateAmount(amount);
        if (!string.Equals(currency, "coins", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"The local wallet adapter does not support currency '{currency}'.");
    }

    private static long Abs(long value) => value == long.MinValue ? long.MaxValue : Math.Abs(value);

    private static LedgerResult Success(
        LedgerOperationKind kind,
        LedgerCommand command,
        string status,
        string? accountId,
        string? referenceId,
        long appliedAmount,
        long? remainingAmount) =>
        new(command.OperationId, kind, ParseStatus(status), true, accountId, referenceId,
            appliedAmount, remainingAmount, command.Currency, null);

    private static LedgerResult Reject(
        LedgerOperationKind kind,
        LedgerCommand command,
        string errorCode,
        string? referenceId) =>
        new(command.OperationId, kind, LedgerOperationStatus.Rejected, false,
            AccountFor(command), referenceId, 0, null, command.Currency, errorCode);

    private static LedgerResult FromOperation(
        LedgerOperationRow operation,
        LedgerOperationKind kind,
        string currency,
        string? accountId,
        string? referenceId) =>
        new(operation.OperationId, kind, ParseStatus(operation.Status),
            !string.Equals(operation.Status, "rejected", StringComparison.Ordinal)
            && !string.Equals(operation.Status, "failed", StringComparison.Ordinal),
            accountId ?? operation.AccountId,
            referenceId ?? operation.ReferenceId,
            operation.AppliedAmount,
            operation.RemainingAmount,
            currency,
            operation.ErrorCode);

    private static LedgerResult FromHold(
        LedgerHoldRow hold,
        LedgerOperationKind kind,
        string currency,
        string accountId,
        string referenceId) =>
        new(hold.OperationId, kind, ParseStatus(hold.Status),
            !string.Equals(hold.Status, "rejected", StringComparison.Ordinal),
            accountId,
            referenceId,
            hold.CapturedAmount + hold.ReleasedAmount,
            hold.AvailableAmount,
            currency,
            null);

    private static string? AccountFor(LedgerCommand command) => command switch
    {
        LedgerTransferRequested transfer => transfer.FromAccountId,
        LedgerHoldRequested hold => hold.AccountId,
        LedgerCaptureRequested capture => capture.AccountId,
        LedgerReleaseRequested release => release.AccountId,
        LedgerRefundRequested refund => refund.AccountId,
        LedgerAdjustmentRequested adjustment => adjustment.AccountId,
        _ => null,
    };

    private static LedgerOperationStatus ParseStatus(string status) => status switch
    {
        "processing" => LedgerOperationStatus.Processing,
        "held" => LedgerOperationStatus.Held,
        "partially_captured" => LedgerOperationStatus.PartiallyCaptured,
        "captured" => LedgerOperationStatus.Captured,
        "partially_released" => LedgerOperationStatus.PartiallyReleased,
        "released" => LedgerOperationStatus.Released,
        "partially_refunded" => LedgerOperationStatus.PartiallyRefunded,
        "refunded" => LedgerOperationStatus.Refunded,
        "completed" => LedgerOperationStatus.Completed,
        "rejected" => LedgerOperationStatus.Rejected,
        "failed" => LedgerOperationStatus.Failed,
        _ => LedgerOperationStatus.Failed,
    };

    private sealed record LedgerOperationRow(
        string OperationId,
        string OperationKind,
        string? AccountId,
        string? TargetAccountId,
        string? ReferenceId,
        long Amount,
        long AppliedAmount,
        long? RemainingAmount,
        string Currency,
        string Status,
        string? ErrorCode)
    {
        public bool IsCompatible(
            LedgerOperationKind kind,
            string? accountId,
            string? targetAccountId,
            string? referenceId,
            long amount,
            string currency) =>
            string.Equals(OperationKind, kind.ToString(), StringComparison.OrdinalIgnoreCase)
            && string.Equals(AccountId, accountId, StringComparison.Ordinal)
            && string.Equals(TargetAccountId, targetAccountId, StringComparison.Ordinal)
            && string.Equals(ReferenceId, referenceId, StringComparison.Ordinal)
            && Amount == amount
            && string.Equals(Currency, currency, StringComparison.Ordinal);
    }

    private sealed record LedgerHoldRow(
        string HoldId,
        string OperationId,
        string AccountId,
        long Amount,
        long CapturedAmount,
        long ReleasedAmount,
        long RefundedAmount,
        string Currency,
        DateTimeOffset ExpiresAt,
        string Status)
    {
        public long AvailableAmount => Amount - CapturedAmount - ReleasedAmount;

        public bool IsCompatible(string operationId, string accountId, long amount, string currency) =>
            string.Equals(OperationId, operationId, StringComparison.Ordinal)
            && string.Equals(AccountId, accountId, StringComparison.Ordinal)
            && Amount == amount
            && string.Equals(Currency, currency, StringComparison.Ordinal);

        public bool IsAccountAndCurrency(string accountId, string currency) =>
            string.Equals(AccountId, accountId, StringComparison.Ordinal)
            && string.Equals(Currency, currency, StringComparison.Ordinal);
    }

    private sealed record WalletRow(string AccountId, long Balance);

    private sealed record LedgerResult(
        string OperationId,
        LedgerOperationKind Kind,
        LedgerOperationStatus Status,
        bool Succeeded,
        string? AccountId,
        string? ReferenceId,
        long AppliedAmount,
        long? RemainingAmount,
        string Currency,
        string? ErrorCode);
}

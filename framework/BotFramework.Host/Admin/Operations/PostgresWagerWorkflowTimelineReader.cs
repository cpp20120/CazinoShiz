using BotFramework.Contracts.Operations;
using BotFramework.Contracts.Tenancy;
using BotFramework.Contracts.Wagering;
using BotFramework.Host.Persistence.Connections;
using Dapper;

namespace BotFramework.Host.Admin.Operations;

/// <summary>
/// Admin-only read model assembled in one database round trip. Nothing here
/// is called by the game or Ledger hot path and it does not write projections.
/// </summary>
public sealed class PostgresWagerWorkflowTimelineReader(
    INpgsqlConnectionFactory connections,
    ITenantContextAccessor tenantContext) : IWagerWorkflowTimelineReader
{
    public async Task<WagerWorkflowTimeline?> GetAsync(string operationId, CancellationToken ct)
    {
        var tenant = tenantContext.Current;
        await using var connection = await connections.OpenAsync(ct);
        using var grid = await connection.QueryMultipleAsync(new CommandDefinition("""
            SELECT operation_id AS OperationId, bet_id AS BetId, game_id AS GameId,
                   player_id AS PlayerId, status AS WagerStatus, updated_at AS UpdatedAt
            FROM wager_operations
            WHERE operation_id = @operationId
            UNION ALL
            SELECT participant.operation_id AS OperationId, participant.bet_id AS BetId,
                   group_state.game_id AS GameId, participant.player_id AS PlayerId,
                   CASE group_state.status
                       WHEN 'reserving' THEN 1 WHEN 'playing' THEN 2
                       WHEN 'settling' THEN 3 WHEN 'completed' THEN 4
                       WHEN 'rejected' THEN 5 WHEN 'failed' THEN 6
                       ELSE 0 END AS WagerStatus,
                   group_state.updated_at AS UpdatedAt
            FROM wager_group_participants participant
            JOIN wager_groups group_state ON group_state.workflow_id = participant.workflow_id
            WHERE participant.operation_id = @operationId
              AND NOT EXISTS (SELECT 1 FROM wager_operations WHERE operation_id = @operationId);

            SELECT stream_id AS StreamId, version AS Version, event_type AS EventType,
                   payload::text AS PayloadJson, occurred_at AS OccurredAt
            FROM module_events
            WHERE stream_id IN (
                SELECT bet_id FROM wager_operations WHERE operation_id = @operationId
                UNION ALL
                SELECT bet_id FROM wager_group_participants WHERE operation_id = @operationId)
            ORDER BY version ASC;

            SELECT type_name AS EventType, payload::text AS PayloadJson,
                   created_at AS OccurredAt
            FROM game_event_outbox
            WHERE payload ->> 'BetId' IN (
                      SELECT bet_id FROM wager_operations WHERE operation_id = @operationId
                      UNION ALL
                      SELECT bet_id FROM wager_group_participants WHERE operation_id = @operationId)
               OR payload ->> 'betId' IN (
                      SELECT bet_id FROM wager_operations WHERE operation_id = @operationId
                      UNION ALL
                      SELECT bet_id FROM wager_group_participants WHERE operation_id = @operationId)
            UNION ALL
            SELECT type_name AS EventType, payload::text AS PayloadJson,
                   created_at AS OccurredAt
            FROM tenant_event_outbox
            WHERE tenant_key = (SELECT tenant_key FROM tenants WHERE tenant_id = @tenantId)
              AND scope_key = (
                  SELECT scope_key FROM tenant_scopes
                  WHERE tenant_key = (SELECT tenant_key FROM tenants WHERE tenant_id = @tenantId)
                    AND scope_id = @scopeId)
              AND (payload ->> 'BetId' IN (
                       SELECT bet_id FROM wager_operations WHERE operation_id = @operationId
                       UNION ALL
                       SELECT bet_id FROM wager_group_participants WHERE operation_id = @operationId)
                   OR payload ->> 'betId' IN (
                       SELECT bet_id FROM wager_operations WHERE operation_id = @operationId
                       UNION ALL
                       SELECT bet_id FROM wager_group_participants WHERE operation_id = @operationId));

            SELECT operation_id AS OperationId, operation_kind AS OperationKind,
                   account_id AS AccountId, target_account_id AS TargetAccountId,
                   reference_id AS ReferenceId, amount AS Amount,
                   applied_amount AS AppliedAmount, remaining_amount AS RemainingAmount,
                   currency AS Currency, status AS Status, error_code AS ErrorCode,
                   updated_at AS UpdatedAt
            FROM ledger_operations
            WHERE tenant_key = (SELECT tenant_key FROM tenants WHERE tenant_id = @tenantId)
              AND scope_key = (
                  SELECT scope_key FROM tenant_scopes
                  WHERE tenant_key = (SELECT tenant_key FROM tenants WHERE tenant_id = @tenantId)
                    AND scope_id = @scopeId)
              AND (operation_id = @operationId
                   OR operation_id = @captureOperationId
                   OR operation_id = @payoutOperationId)
            ORDER BY updated_at ASC, operation_id ASC;
            """, new
        {
            operationId,
            captureOperationId = $"{operationId}:capture",
            payoutOperationId = $"{operationId}:payout",
            tenantId = tenant?.TenantId.Value ?? string.Empty,
            scopeId = tenant?.ScopeId.Value ?? string.Empty,
        }, cancellationToken: ct));

        var operation = await grid.ReadSingleOrDefaultAsync<OperationRow>();
        if (operation is null)
            return null;

        var wagerStatus = ((WagerOperationStatus)operation.WagerStatus).ToString();
        var steps = new List<WagerWorkflowStep>
        {
            new("wagering", "wager.accepted", wagerStatus,
                operation.OperationId, null, operation.UpdatedAt, null),
        };
        steps.AddRange((await grid.ReadAsync<EventRow>()).Select(@event => new WagerWorkflowStep(
            "es",
            @event.EventType,
            "committed",
            operation.OperationId,
            @event.Version,
            @event.OccurredAt,
            @event.PayloadJson)));
        steps.AddRange((await grid.ReadAsync<GameEventOutboxRow>()).Select(@event => new WagerWorkflowStep(
            "es",
            @event.EventType,
            "committed",
            operation.OperationId,
            null,
            @event.OccurredAt,
            @event.PayloadJson)));
        steps.AddRange((await grid.ReadAsync<LedgerRow>()).Select(ledger => new WagerWorkflowStep(
            "ledger",
            $"ledger.{ledger.OperationKind}",
            ledger.Status,
            ledger.OperationId,
            null,
            ledger.UpdatedAt,
            System.Text.Json.JsonSerializer.Serialize(new
            {
                ledger.AccountId,
                ledger.TargetAccountId,
                ledger.ReferenceId,
                ledger.Amount,
                ledger.AppliedAmount,
                ledger.RemainingAmount,
                ledger.Currency,
                ledger.ErrorCode,
            }))));

        steps.Sort(static (left, right) => left.OccurredAt.CompareTo(right.OccurredAt));
        return new WagerWorkflowTimeline(
            operation.OperationId,
            operation.BetId,
            operation.GameId,
            operation.PlayerId,
            wagerStatus,
            operation.UpdatedAt,
            steps);
    }

    private sealed record OperationRow(
        string OperationId,
        string BetId,
        string GameId,
        string PlayerId,
        int WagerStatus,
        DateTimeOffset UpdatedAt);

    private sealed record EventRow(
        string StreamId,
        long Version,
        string EventType,
        string PayloadJson,
        DateTimeOffset OccurredAt);

    private sealed record LedgerRow(
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
        string? ErrorCode,
        DateTimeOffset UpdatedAt);

    private sealed record GameEventOutboxRow(
        string EventType,
        string PayloadJson,
        DateTimeOffset OccurredAt);
}

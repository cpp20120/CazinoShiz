using System.Text.Json;
using BotFramework.Contracts.Wagering;
using BotFramework.Host.Persistence.Connections;
using Dapper;

namespace BotFramework.Host.Wagering;

public sealed class PostgresMultiPartyWagerStore(INpgsqlConnectionFactory connections)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task EnsureCreatedAsync(MultiPartyWagerRequest request, CancellationToken ct)
    {
        if (request.Participants.Count < 2)
            throw new ArgumentException("A multi-party wager requires at least two participants.", nameof(request));

        await using var connection = await connections.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO wager_groups
                (workflow_id, game_id, game_input, tenant_id, scope_id, status)
            VALUES
                (@WorkflowId, @GameId, CAST(@GameInput AS jsonb), @TenantId, @ScopeId, 'reserving')
            ON CONFLICT (workflow_id) DO NOTHING
            """,
            request,
            transaction,
            cancellationToken: ct));
        var group = await connection.QuerySingleAsync<MultiPartyWagerGroupIdentity>(new CommandDefinition(
            """
            SELECT game_id AS GameId, game_input::text AS GameInput,
                   tenant_id AS TenantId, scope_id AS ScopeId
            FROM wager_groups
            WHERE workflow_id = @WorkflowId
            FOR UPDATE
            """,
            new { request.WorkflowId },
            transaction,
            cancellationToken: ct));
        if (!string.Equals(group.GameId, request.GameId, StringComparison.Ordinal)
            || !JsonEquals(group.GameInput, request.GameInput)
            || !string.Equals(group.TenantId, request.TenantId, StringComparison.Ordinal)
            || !string.Equals(group.ScopeId, request.ScopeId, StringComparison.Ordinal))
            throw new InvalidOperationException("multiparty_workflow_conflict");

        foreach (var participant in request.Participants)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO wager_group_participants
                    (workflow_id, operation_id, bet_id, player_id, amount, currency,
                     rules_version, settlement_rule, status)
                VALUES
                    (@WorkflowId, @OperationId, @BetId, @PlayerId, @Stake, @Currency,
                     @RulesVersion, @SettlementRule, 'pending')
                ON CONFLICT (workflow_id, bet_id) DO NOTHING
                """,
                new
                {
                    request.WorkflowId,
                    participant.OperationId,
                    participant.BetId,
                    participant.PlayerId,
                    participant.Stake,
                    participant.Currency,
                    participant.Terms.RulesVersion,
                    participant.Terms.SettlementRule,
                },
                transaction,
                cancellationToken: ct));
        }
        var existingParticipants = (await connection.QueryAsync<MultiPartyWagerParticipantState>(new CommandDefinition(
            """
            SELECT operation_id AS OperationId, bet_id AS BetId, player_id AS PlayerId,
                   amount AS Stake, currency AS Currency, status AS Status,
                   rules_version AS RulesVersion, settlement_rule AS SettlementRule,
                   outcome_code AS OutcomeCode, payout AS Payout, error_code AS ErrorCode
            FROM wager_group_participants
            WHERE workflow_id = @WorkflowId
            """,
            new { request.WorkflowId },
            transaction,
            cancellationToken: ct))).ToArray();
        if (existingParticipants.Length != request.Participants.Count
            || request.Participants.Any(participant => existingParticipants.All(existing =>
                !string.Equals(existing.OperationId, participant.OperationId, StringComparison.Ordinal)
                || !string.Equals(existing.BetId, participant.BetId, StringComparison.Ordinal)
                || !string.Equals(existing.PlayerId, participant.PlayerId, StringComparison.Ordinal)
                || existing.Stake != participant.Stake
                || !string.Equals(existing.Currency, participant.Currency, StringComparison.Ordinal)
                || !string.Equals(existing.RulesVersion, participant.Terms.RulesVersion, StringComparison.Ordinal)
                || !string.Equals(existing.SettlementRule, participant.Terms.SettlementRule, StringComparison.Ordinal))))
            throw new InvalidOperationException("multiparty_workflow_conflict");

        await transaction.CommitAsync(ct);
    }

    public async Task SetGroupStatusAsync(
        string workflowId,
        string status,
        string? errorCode,
        IReadOnlyList<MultiPartyWagerOutcome>? outcomes,
        CancellationToken ct)
    {
        await using var connection = await connections.OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE wager_groups
            SET status = @Status,
                error_code = @ErrorCode,
                outcome_json = CASE WHEN @OutcomeJson IS NULL THEN outcome_json ELSE CAST(@OutcomeJson AS jsonb) END,
                updated_at = now()
            WHERE workflow_id = @WorkflowId
            """,
            new
            {
                WorkflowId = workflowId,
                Status = status,
                ErrorCode = errorCode,
                OutcomeJson = outcomes is null ? null : JsonSerializer.Serialize(outcomes, JsonOptions),
            },
            cancellationToken: ct));
    }

    public async Task SetParticipantStatusAsync(
        string workflowId,
        string betId,
        string status,
        string? outcomeCode,
        long? payout,
        string? errorCode,
        CancellationToken ct)
    {
        await using var connection = await connections.OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE wager_group_participants
            SET status = @Status, outcome_code = @OutcomeCode, payout = @Payout,
                error_code = @ErrorCode
            WHERE workflow_id = @WorkflowId AND bet_id = @BetId
            """,
            new { WorkflowId = workflowId, BetId = betId, Status = status, OutcomeCode = outcomeCode, Payout = payout, ErrorCode = errorCode },
            cancellationToken: ct));
    }

    public async Task<IReadOnlyList<MultiPartyWagerParticipantState>> GetParticipantsAsync(
        string workflowId,
        CancellationToken ct)
    {
        await using var connection = await connections.OpenAsync(ct);
        var rows = await connection.QueryAsync<MultiPartyWagerParticipantState>(new CommandDefinition(
            """
            SELECT operation_id AS OperationId, bet_id AS BetId, player_id AS PlayerId,
                   amount AS Stake, currency AS Currency, status AS Status,
                   rules_version AS RulesVersion, settlement_rule AS SettlementRule,
                   outcome_code AS OutcomeCode, payout AS Payout, error_code AS ErrorCode
            FROM wager_group_participants
            WHERE workflow_id = @WorkflowId
            ORDER BY bet_id
            """,
            new { WorkflowId = workflowId },
            cancellationToken: ct));
        return rows.ToArray();
    }

    public async Task<string?> GetReservationStatusAsync(string operationId, CancellationToken ct)
    {
        await using var connection = await connections.OpenAsync(ct);
        return await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            "SELECT status FROM wager_reservations WHERE operation_id = @OperationId",
            new { OperationId = operationId },
            cancellationToken: ct));
    }

    public async Task<MultiPartyWagerResult?> GetResultAsync(
        string workflowId,
        string tenantId,
        string scopeId,
        CancellationToken ct)
    {
        await using var connection = await connections.OpenAsync(ct);
        var row = await connection.QuerySingleOrDefaultAsync<MultiPartyWagerResultRow>(new CommandDefinition(
            """
            SELECT status, error_code AS ErrorCode, outcome_json::text AS OutcomeJson
            FROM wager_groups
            WHERE workflow_id = @WorkflowId AND tenant_id = @TenantId AND scope_id = @ScopeId
            """,
            new { WorkflowId = workflowId, TenantId = tenantId, ScopeId = scopeId },
            cancellationToken: ct));
        if (row is null)
            return null;

        var outcomes = string.IsNullOrWhiteSpace(row.OutcomeJson)
            ? []
            : JsonSerializer.Deserialize<IReadOnlyList<MultiPartyWagerOutcome>>(row.OutcomeJson, JsonOptions) ?? [];
        return new MultiPartyWagerResult(workflowId, row.Status, outcomes, row.ErrorCode);
    }

    private sealed record MultiPartyWagerResultRow(
        string Status,
        string? ErrorCode,
        string? OutcomeJson);

    private sealed record MultiPartyWagerGroupIdentity(
        string GameId,
        string GameInput,
        string TenantId,
        string ScopeId);

    private static bool JsonEquals(string left, string right)
    {
        using var leftDocument = JsonDocument.Parse(left);
        using var rightDocument = JsonDocument.Parse(right);
        return JsonElement.DeepEquals(leftDocument.RootElement, rightDocument.RootElement);
    }
}

public sealed record MultiPartyWagerParticipantState(
    string OperationId,
    string BetId,
    string PlayerId,
    long Stake,
    string Currency,
    string Status,
    string RulesVersion,
    string SettlementRule,
    string? OutcomeCode,
    long? Payout,
    string? ErrorCode);

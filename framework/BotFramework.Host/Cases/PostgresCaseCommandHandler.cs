using System.Data;
using System.Data.Common;
using System.Text.Json;
using BotFramework.Contracts.Cases;
using BotFramework.Contracts.Messaging;
using BotFramework.Contracts.Tenancy;
using BotFramework.Host.Persistence.Connections;
using Dapper;

namespace BotFramework.Host.Cases;

/// <summary>
/// Durable generic Case aggregate. Domain-specific policies decide why a case
/// exists; this adapter persists lifecycle transitions and emits integration
/// events through the configured outbox transport.
/// </summary>
public sealed class PostgresCaseCommandHandler(
    INpgsqlConnectionFactory connections,
    IIntegrationInboxContextAccessor inboxContext,
    ITenantContextAccessor tenantContext,
    IIntegrationEventPublisher events)
    : IIntegrationCommandHandler<CaseOpenRequested>,
      IIntegrationCommandHandler<CaseEvidenceRequested>,
      IIntegrationCommandHandler<CaseReviewRequested>,
      IIntegrationCommandHandler<CaseResolveRequested>,
      IIntegrationCommandHandler<CaseAppealRequested>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task HandleAsync(CaseOpenRequested command, CancellationToken ct) =>
        ExecuteAsync(async (connection, transaction, tenant) =>
        {
            var existing = await LoadAsync(connection, transaction, tenant, command.CaseId, ct);
            if (existing is not null)
            {
                if (!string.Equals(existing.CaseType, command.CaseType, StringComparison.Ordinal)
                    || !string.Equals(existing.SubjectId, command.SubjectId, StringComparison.Ordinal)
                    || !string.Equals(existing.Reason, command.Reason, StringComparison.Ordinal))
                    return Rejected(command, "case_conflict");
                return new CaseResult(existing, new CaseOpened(existing));
            }

            var transition = CaseLifecycle.Open(command.CaseId, command.CaseType, command.SubjectId,
                command.OpenedBy, command.Reason, command.OccurredAt);
            if (!transition.Accepted) return Rejected(command, transition.ErrorCode!);
            await InsertAsync(connection, transaction, tenant, transition.State!, ct);
            return new CaseResult(transition.State!, new CaseOpened(transition.State!));
        }, ct);

    public Task HandleAsync(CaseEvidenceRequested command, CancellationToken ct) =>
        ExecuteAsync(async (connection, transaction, tenant) =>
        {
            var state = await LoadAsync(connection, transaction, tenant, command.CaseId, ct);
            if (state is null) return Rejected(command, "case_not_found");
            var transition = CaseLifecycle.AddEvidence(state, command.Evidence);
            if (!transition.Accepted) return Rejected(command, transition.ErrorCode!);
            if (ReferenceEquals(transition.State, state)) return new CaseResult(state, null);
            await UpdateAsync(connection, transaction, tenant, transition.State!, state.Version, ct);
            return new CaseResult(transition.State!, new CaseEvidenceAdded(command.CaseId, command.Evidence, command.OccurredAt));
        }, ct);

    public Task HandleAsync(CaseReviewRequested command, CancellationToken ct) =>
        ExecuteAsync(async (connection, transaction, tenant) =>
        {
            var state = await LoadAsync(connection, transaction, tenant, command.CaseId, ct);
            if (state is null) return Rejected(command, "case_not_found");
            var transition = CaseLifecycle.StartReview(state, command.ReviewerId);
            if (!transition.Accepted) return Rejected(command, transition.ErrorCode!);
            await UpdateAsync(connection, transaction, tenant, transition.State!, state.Version, ct);
            return new CaseResult(transition.State!, new CaseReviewStarted(command.CaseId, command.ReviewerId, command.OccurredAt));
        }, ct);

    public Task HandleAsync(CaseResolveRequested command, CancellationToken ct) =>
        ExecuteAsync(async (connection, transaction, tenant) =>
        {
            var state = await LoadAsync(connection, transaction, tenant, command.CaseId, ct);
            if (state is null) return Rejected(command, "case_not_found");
            var transition = CaseLifecycle.Resolve(state, command.ResolverId, command.ResolutionCode,
                command.Notes, command.OccurredAt);
            if (!transition.Accepted) return Rejected(command, transition.ErrorCode!);
            await UpdateAsync(connection, transaction, tenant, transition.State!, state.Version, ct);
            return new CaseResult(transition.State!, new CaseResolved(command.CaseId, command.ResolverId,
                command.ResolutionCode, command.OccurredAt));
        }, ct);

    public Task HandleAsync(CaseAppealRequested command, CancellationToken ct) =>
        ExecuteAsync(async (connection, transaction, tenant) =>
        {
            var state = await LoadAsync(connection, transaction, tenant, command.CaseId, ct);
            if (state is null) return Rejected(command, "case_not_found");
            var transition = CaseLifecycle.Appeal(state, command.Reason, command.OccurredAt);
            if (!transition.Accepted) return Rejected(command, transition.ErrorCode!);
            await UpdateAsync(connection, transaction, tenant, transition.State!, state.Version, ct);
            return new CaseResult(transition.State!, new CaseAppealed(command.CaseId, command.Reason, command.OccurredAt));
        }, ct);

    private async Task ExecuteAsync(
        Func<DbConnection, DbTransaction, TenantContext, Task<CaseResult>> action,
        CancellationToken ct)
    {
        var tenant = RequireTenant();
        var result = await InTransactionAsync((connection, transaction) => action(connection, transaction, tenant), ct);
        if (result.Event is not null)
            await events.PublishAsync(result.Event, ct);
        else if (result.Rejection is not null)
            await events.PublishAsync(result.Rejection, ct);
    }

    private static async Task<CaseState?> LoadAsync(
        DbConnection connection,
        DbTransaction transaction,
        TenantContext tenant,
        string caseId,
        CancellationToken ct)
    {
        var json = await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            """
            SELECT state_json
            FROM framework_cases
            WHERE tenant_key = (SELECT tenant_key FROM tenants WHERE tenant_id = @tenantId)
              AND scope_key = (
                  SELECT scope_key FROM tenant_scopes
                  WHERE tenant_key = (SELECT tenant_key FROM tenants WHERE tenant_id = @tenantId)
                    AND scope_id = @scopeId)
              AND case_id = @caseId
            FOR UPDATE
            """,
            new { tenantId = tenant.TenantId.Value, scopeId = tenant.ScopeId.Value, caseId },
            transaction,
            cancellationToken: ct));
        return json is null ? null : JsonSerializer.Deserialize<CaseState>(json, JsonOptions);
    }

    private static Task<int> InsertAsync(
        DbConnection connection,
        DbTransaction transaction,
        TenantContext tenant,
        CaseState state,
        CancellationToken ct) =>
        connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO framework_cases
                (tenant_key, scope_key, case_id, case_type, subject_id, status, state_json, version)
            SELECT t.tenant_key, s.scope_key, @caseId, @caseType, @subjectId, @status, @stateJson, @version
            FROM tenants t
            JOIN tenant_scopes s ON s.tenant_key = t.tenant_key AND s.scope_id = @scopeId
            WHERE t.tenant_id = @tenantId
            """,
            new
            {
                tenantId = tenant.TenantId.Value,
                scopeId = tenant.ScopeId.Value,
                caseId = state.CaseId,
                caseType = state.CaseType,
                subjectId = state.SubjectId,
                status = state.Status.ToString().ToLowerInvariant(),
                stateJson = JsonSerializer.Serialize(state, JsonOptions),
                state.Version,
            },
            transaction,
            cancellationToken: ct));

    private static Task<int> UpdateAsync(
        DbConnection connection,
        DbTransaction transaction,
        TenantContext tenant,
        CaseState state,
        long expectedVersion,
        CancellationToken ct) =>
        connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE framework_cases
            SET status = @status, state_json = @stateJson, version = @version, updated_at = now()
            WHERE tenant_key = (SELECT tenant_key FROM tenants WHERE tenant_id = @tenantId)
              AND scope_key = (
                  SELECT scope_key FROM tenant_scopes
                  WHERE tenant_key = (SELECT tenant_key FROM tenants WHERE tenant_id = @tenantId)
                    AND scope_id = @scopeId)
              AND case_id = @caseId
              AND version = @expectedVersion
            """,
            new
            {
                tenantId = tenant.TenantId.Value,
                scopeId = tenant.ScopeId.Value,
                caseId = state.CaseId,
                status = state.Status.ToString().ToLowerInvariant(),
                stateJson = JsonSerializer.Serialize(state, JsonOptions),
                version = state.Version,
                expectedVersion,
            },
            transaction,
            cancellationToken: ct));

    private static CaseResult Rejected(CaseCommand command, string errorCode) =>
        new(null, null, new CaseTransitionRejected(command.OperationId, command.CaseId, errorCode, command.OccurredAt));

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
        ?? throw new InvalidOperationException("Case requires tenant context.");

    private sealed record CaseResult(
        CaseState? State,
        CaseEvent? Event,
        CaseTransitionRejected? Rejection = null);
}

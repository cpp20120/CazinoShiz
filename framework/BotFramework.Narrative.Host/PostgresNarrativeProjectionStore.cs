using System.Text.Json;
using BotFramework.Host.Persistence.Connections;
using Dapper;

namespace BotFramework.Narrative.Host;

/// <summary>
/// PostgreSQL projection for resumable narrative state. Each durable outbox
/// delivery is recorded in the same projection transaction, making retries
/// semantically idempotent before a frontend sink is invoked again.
/// </summary>
public sealed class PostgresNarrativeProjectionStore(
    INpgsqlConnectionFactory connections,
    TimeProvider timeProvider) : INarrativeProjectionStore, INarrativeProjectionWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<NarrativeProjection?> GetAsync(NarrativeProjectionKey key, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(key);
        var identity = ProjectionIdentity.From(key);
        await using var connection = await connections.OpenAsync(ct);
        var row = await connection.QuerySingleOrDefaultAsync<ProjectionRow>(new CommandDefinition(
            """
            SELECT flags::text AS FlagsJson,
                   checkpoint::text AS CheckpointJson,
                   active_choice::text AS ActiveChoiceJson,
                   revision AS Revision,
                   updated_at AS UpdatedAt
            FROM narrative_projections
            WHERE tenant_id = @TenantId
              AND scope_id = @ScopeId
              AND conversation_id = @ConversationId
              AND recipient_id = @RecipientId
            """,
            identity,
            cancellationToken: ct));
        if (row is null)
            return null;

        var activeChoice = Deserialize<NarrativeActiveChoice>(row.ActiveChoiceJson);
        if (activeChoice?.ExpiresAt is { } expiresAt && expiresAt <= timeProvider.GetUtcNow())
            activeChoice = null;

        return new NarrativeProjection(
            key,
            Deserialize<Dictionary<string, bool>>(row.FlagsJson) ?? [],
            Deserialize<NarrativeCheckpoint>(row.CheckpointJson),
            activeChoice,
            row.Revision,
            new DateTimeOffset(DateTime.SpecifyKind(row.UpdatedAt, DateTimeKind.Utc)));
    }

    public async Task ApplyAsync(NarrativeEffect effect, NarrativeDeliveryContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(effect);
        ArgumentNullException.ThrowIfNull(context);
        if (effect is not (NarrativeFlagEffect or NarrativeCheckpointEffect or ChoiceEffect))
            return;

        var key = new NarrativeProjectionKey(effect.Target, ScopeFrom(context));
        var identity = ProjectionIdentity.From(key);
        await using var connection = await connections.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        if (!await TryBeginDeliveryAsync(connection, transaction, context.DeliveryId, ct))
        {
            await transaction.CommitAsync(ct);
            return;
        }

        await EnsureProjectionAsync(connection, transaction, identity, ct);
        var updatedAt = timeProvider.GetUtcNow();
        switch (effect)
        {
            case NarrativeFlagEffect flag:
                await ApplyFlagAsync(connection, transaction, identity, flag, updatedAt, ct);
                break;
            case NarrativeCheckpointEffect checkpoint:
                await ApplyCheckpointAsync(connection, transaction, identity, checkpoint, updatedAt, ct);
                break;
            case ChoiceEffect choice:
                await ApplyChoiceAsync(connection, transaction, identity, choice, updatedAt, ct);
                break;
        }

        await transaction.CommitAsync(ct);
    }

    private static async Task<bool> TryBeginDeliveryAsync(
        Npgsql.NpgsqlConnection connection,
        Npgsql.NpgsqlTransaction transaction,
        string? deliveryId,
        CancellationToken ct)
    {
        if (deliveryId is null)
            return true;

        var inserted = await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO narrative_projection_deliveries (delivery_id)
            VALUES (@deliveryId)
            ON CONFLICT (delivery_id) DO NOTHING
            """,
            new { deliveryId },
            transaction,
            cancellationToken: ct));
        return inserted == 1;
    }

    private static Task<int> EnsureProjectionAsync(
        Npgsql.NpgsqlConnection connection,
        Npgsql.NpgsqlTransaction transaction,
        ProjectionIdentity identity,
        CancellationToken ct) =>
        connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO narrative_projections (
                tenant_id, scope_id, conversation_id, recipient_id)
            VALUES (@TenantId, @ScopeId, @ConversationId, @RecipientId)
            ON CONFLICT (tenant_id, scope_id, conversation_id, recipient_id) DO NOTHING
            """,
            identity,
            transaction,
            cancellationToken: ct));

    private static Task<int> ApplyFlagAsync(
        Npgsql.NpgsqlConnection connection,
        Npgsql.NpgsqlTransaction transaction,
        ProjectionIdentity identity,
        NarrativeFlagEffect effect,
        DateTimeOffset updatedAt,
        CancellationToken ct)
    {
        var sql = effect.Value
            ? """
              UPDATE narrative_projections
              SET flags = jsonb_set(flags, ARRAY[@Flag]::text[], 'true'::jsonb, true),
                  revision = revision + 1,
                  updated_at = @UpdatedAt
              WHERE tenant_id = @TenantId
                AND scope_id = @ScopeId
                AND conversation_id = @ConversationId
                AND recipient_id = @RecipientId
              """
            : """
              UPDATE narrative_projections
              SET flags = flags - @Flag,
                  revision = revision + 1,
                  updated_at = @UpdatedAt
              WHERE tenant_id = @TenantId
                AND scope_id = @ScopeId
                AND conversation_id = @ConversationId
                AND recipient_id = @RecipientId
              """;
        return connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                identity.TenantId,
                identity.ScopeId,
                identity.ConversationId,
                identity.RecipientId,
                effect.Flag,
                updatedAt,
            },
            transaction,
            cancellationToken: ct));
    }

    private static Task<int> ApplyCheckpointAsync(
        Npgsql.NpgsqlConnection connection,
        Npgsql.NpgsqlTransaction transaction,
        ProjectionIdentity identity,
        NarrativeCheckpointEffect effect,
        DateTimeOffset updatedAt,
        CancellationToken ct)
    {
        var checkpoint = JsonSerializer.Serialize(
            new NarrativeCheckpoint(effect.CheckpointId, effect.ResumeToken, effect.Data),
            JsonOptions);
        return connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE narrative_projections
            SET checkpoint = CAST(@checkpoint AS jsonb),
                revision = revision + 1,
                updated_at = @UpdatedAt
            WHERE tenant_id = @TenantId
              AND scope_id = @ScopeId
              AND conversation_id = @ConversationId
              AND recipient_id = @RecipientId
            """,
            new
            {
                identity.TenantId,
                identity.ScopeId,
                identity.ConversationId,
                identity.RecipientId,
                checkpoint,
                updatedAt,
            },
            transaction,
            cancellationToken: ct));
    }

    private static Task<int> ApplyChoiceAsync(
        Npgsql.NpgsqlConnection connection,
        Npgsql.NpgsqlTransaction transaction,
        ProjectionIdentity identity,
        ChoiceEffect effect,
        DateTimeOffset updatedAt,
        CancellationToken ct)
    {
        var activeChoice = JsonSerializer.Serialize(
            new NarrativeActiveChoice(
                effect.InteractionId,
                effect.Options,
                effect.ExpiresAt,
                effect.AllowsMultiple),
            JsonOptions);
        return connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE narrative_projections
            SET active_choice = CAST(@activeChoice AS jsonb),
                active_choice_expires_at = @ExpiresAt,
                revision = revision + 1,
                updated_at = @UpdatedAt
            WHERE tenant_id = @TenantId
              AND scope_id = @ScopeId
              AND conversation_id = @ConversationId
              AND recipient_id = @RecipientId
            """,
            new
            {
                identity.TenantId,
                identity.ScopeId,
                identity.ConversationId,
                identity.RecipientId,
                activeChoice,
                effect.ExpiresAt,
                updatedAt,
            },
            transaction,
            cancellationToken: ct));
    }

    private static NarrativeProjectionScope? ScopeFrom(NarrativeDeliveryContext context)
    {
        var hasTenant = context.Metadata.TryGetValue("tenant.id", out var tenantId)
            && !string.IsNullOrWhiteSpace(tenantId);
        var hasScope = context.Metadata.TryGetValue("scope.id", out var scopeId)
            && !string.IsNullOrWhiteSpace(scopeId);
        if (!hasTenant && !hasScope)
            return null;
        if (!hasTenant || !hasScope)
            throw new InvalidOperationException("Narrative projection delivery must provide both tenant.id and scope.id.");

        return new NarrativeProjectionScope(tenantId!, scopeId!);
    }

    private static T? Deserialize<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return default;
        return JsonSerializer.Deserialize<T>(json, JsonOptions);
    }

    private sealed record ProjectionIdentity(
        string TenantId,
        string ScopeId,
        string ConversationId,
        string RecipientId)
    {
        public static ProjectionIdentity From(NarrativeProjectionKey key) => new(
            key.Scope?.TenantId ?? string.Empty,
            key.Scope?.ScopeId ?? string.Empty,
            key.Target.ConversationId,
            key.Target.RecipientId ?? string.Empty);
    }

    private sealed record ProjectionRow(
        string FlagsJson,
        string? CheckpointJson,
        string? ActiveChoiceJson,
        long Revision,
        DateTime UpdatedAt);
}

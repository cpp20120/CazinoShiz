using BotFramework.Host;
using Dapper;

namespace Games.Meta;

public interface IRiskStore
{
    Task UpsertOpenAsync(MetaSeason season, long chatId, long userId, string displayName, string kind, string severity, string reason, string evidenceJson, CancellationToken ct);
    Task<IReadOnlyList<RiskFlagView>> GetOpenAsync(MetaSeason season, long chatId, int limit, CancellationToken ct);
    Task<RiskResolveResult> UpdateStatusAsync(long flagId, string status, CancellationToken ct);
}

public sealed class RiskStore(INpgsqlConnectionFactory connections) : IRiskStore
{
    public async Task UpsertOpenAsync(MetaSeason season, long chatId, long userId, string displayName, string kind, string severity, string reason, string evidenceJson, CancellationToken ct)
    {
        const string sql = """
            INSERT INTO meta_risk_flags (season_id, chat_id, user_id, display_name, kind, severity, reason, evidence)
            VALUES (@seasonId, @chatId, @userId, @displayName, @kind, @severity, @reason, CAST(@evidenceJson AS jsonb))
            ON CONFLICT (season_id, chat_id, user_id, kind, status)
            WHERE status = 'open'
            DO UPDATE SET display_name = EXCLUDED.display_name,
                          severity = EXCLUDED.severity,
                          reason = EXCLUDED.reason,
                          evidence = EXCLUDED.evidence,
                          updated_at = now()
            """;

        await using var conn = await connections.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(sql, new { seasonId = season.Id, chatId, userId, displayName, kind, severity, reason, evidenceJson }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<RiskFlagView>> GetOpenAsync(MetaSeason season, long chatId, int limit, CancellationToken ct)
    {
        const string sql = """
            SELECT id,
                   chat_id AS ChatId,
                   user_id AS UserId,
                   display_name AS DisplayName,
                   kind,
                   severity,
                   status,
                   reason,
                   created_at AS CreatedAt
            FROM meta_risk_flags
            WHERE season_id = @seasonId AND chat_id = @chatId AND status = 'open'
            ORDER BY created_at DESC
            LIMIT @limit
            """;

        await using var conn = await connections.OpenAsync(ct);
        var rows = await conn.QueryAsync<RiskFlagView>(new CommandDefinition(sql, new { seasonId = season.Id, chatId, limit = Math.Clamp(limit, 1, 100) }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<RiskResolveResult> UpdateStatusAsync(long flagId, string status, CancellationToken ct)
    {
        status = status.ToLowerInvariant() switch
        {
            "ignore" => "ignored",
            "ignored" => "ignored",
            "resolve" => "resolved",
            "resolved" => "resolved",
            _ => ""
        };
        if (string.IsNullOrWhiteSpace(status)) return new RiskResolveResult(false, "Статус должен быть ignored/resolved.");

        const string sql = """
            UPDATE meta_risk_flags
            SET status = @status,
                resolved_at = now(),
                updated_at = now()
            WHERE id = @flagId AND status = 'open'
            """;

        await using var conn = await connections.OpenAsync(ct);
        var changed = await conn.ExecuteAsync(new CommandDefinition(sql, new { flagId, status }, cancellationToken: ct));
        return changed > 0
            ? new RiskResolveResult(true, "Risk flag обновлён.")
            : new RiskResolveResult(false, "Открытый risk flag не найден.");
    }
}

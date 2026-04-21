using System.Text.Json;
using Dapper;

namespace BotFramework.Host.Services;

public interface IAdminAuditLog
{
    Task LogAsync(long actorId, string actorName, string action, object? details = null, CancellationToken ct = default);
}

public sealed class AdminAuditLog(INpgsqlConnectionFactory connections) : IAdminAuditLog
{
    public async Task LogAsync(long actorId, string actorName, string action, object? details = null, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition("""
            INSERT INTO admin_audit (actor_id, actor_name, action, details, occurred_at)
            VALUES (@actorId, @actorName, @action, @details::jsonb, now())
            """,
            new
            {
                actorId,
                actorName,
                action,
                details = details is null ? "{}" : JsonSerializer.Serialize(details)
            },
            cancellationToken: ct));
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// ChatsStore — read-only query layer for the admin /chats command.
//
// Joins the framework-owned `known_chats` table (populated by
// KnownChatsMiddleware on every update) with the `users` table so the admin
// list shows engagement at a glance: how many wallets that chat has and the
// summed balance per chat. No mutation methods live here — the migrations
// for `known_chats` itself ship inside the framework.
// ─────────────────────────────────────────────────────────────────────────────

using BotFramework.Host;
using Dapper;

namespace Games.Admin;

public interface IChatsStore
{
    Task<IReadOnlyList<KnownChatRow>> ListChatsAsync(string? typeFilter, int limit, CancellationToken ct);
    Task<int> CountChatsAsync(string? typeFilter, CancellationToken ct);
}

public sealed record KnownChatRow(
    long ChatId,
    string ChatType,
    string? Title,
    string? Username,
    DateTime FirstSeenAt,
    DateTime LastSeenAt,
    int UserCount,
    long TotalCoins);

public sealed class ChatsStore(INpgsqlConnectionFactory connections) : IChatsStore
{
    public async Task<IReadOnlyList<KnownChatRow>> ListChatsAsync(
        string? typeFilter, int limit, CancellationToken ct)
    {
        // We can't use `WHERE chat_type = ANY(@types)` directly via Dapper for
        // an empty/null filter — splitting at the SQL level keeps the planner
        // happy with the partial index on chat_type for typed queries and
        // dodges that branch entirely for "show everything".
        var typeClause = typeFilter is null ? "" : "WHERE kc.chat_type = ANY(@types)";
        var limitClause = limit > 0 ? "LIMIT @limit" : "";

        var sql = $"""
            SELECT kc.chat_id        AS ChatId,
                   kc.chat_type      AS ChatType,
                   kc.title          AS Title,
                   kc.username       AS Username,
                   kc.first_seen_at  AS FirstSeenAt,
                   kc.last_seen_at   AS LastSeenAt,
                   COALESCE(u.user_count, 0)  AS UserCount,
                   COALESCE(u.total_coins, 0) AS TotalCoins
            FROM known_chats kc
            LEFT JOIN (
                SELECT balance_scope_id,
                       count(*)::INT  AS user_count,
                       sum(coins)::BIGINT AS total_coins
                FROM users
                GROUP BY balance_scope_id
            ) u ON u.balance_scope_id = kc.chat_id
            {typeClause}
            ORDER BY kc.last_seen_at DESC
            {limitClause}
            """;

        await using var conn = await connections.OpenAsync(ct);
        var rows = await conn.QueryAsync<KnownChatRow>(new CommandDefinition(
            sql, new { types = ResolveTypes(typeFilter), limit }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<int> CountChatsAsync(string? typeFilter, CancellationToken ct)
    {
        var typeClause = typeFilter is null ? "" : "WHERE chat_type = ANY(@types)";
        var sql = $"SELECT count(*)::INT FROM known_chats {typeClause}";

        await using var conn = await connections.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            sql, new { types = ResolveTypes(typeFilter) }, cancellationToken: ct));
    }

    private static string[] ResolveTypes(string? typeFilter) => typeFilter switch
    {
        null => [],
        "private" => ["private"],
        "group" => ["group", "supergroup"],
        "channel" => ["channel"],
        _ => [typeFilter],
    };
}

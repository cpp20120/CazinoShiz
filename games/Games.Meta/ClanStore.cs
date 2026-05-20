using BotFramework.Host;
using Dapper;

namespace Games.Meta;

public interface IClanStore
{
    Task<ClanCreateResult> CreateAsync(MetaSeason season, long chatId, long userId, string displayName, string tag, string name, CancellationToken ct);
    Task<ClanJoinResult> JoinAsync(MetaSeason season, long chatId, long userId, string displayName, string tag, CancellationToken ct);
    Task<ClanInfo?> GetUserClanAsync(MetaSeason season, long chatId, long userId, CancellationToken ct);
    Task<ClanInfo?> GetClanByTagAsync(MetaSeason season, long chatId, string tag, CancellationToken ct);
    Task<IReadOnlyList<ClanMemberInfo>> GetMembersAsync(long clanId, CancellationToken ct);
    Task<IReadOnlyList<ClanLeaderboardEntry>> GetTopAsync(MetaSeason season, long chatId, int limit, CancellationToken ct);
    Task ApplyGameCompletedAsync(MetaSeason season, GameCompletedMetaEvent ev, long xpDelta, CancellationToken ct);
}

public sealed class ClanStore(INpgsqlConnectionFactory connections) : IClanStore
{
    public async Task<ClanCreateResult> CreateAsync(
        MetaSeason season,
        long chatId,
        long userId,
        string displayName,
        string tag,
        string name,
        CancellationToken ct)
    {
        tag = NormalizeTag(tag);
        name = name.Trim();
        if (!IsValidTag(tag)) return new ClanCreateResult(false, "Тег должен быть от 2 до 12 символов: буквы, цифры, _, -.");
        if (name.Length is < 2 or > 64) return new ClanCreateResult(false, "Название клана должно быть от 2 до 64 символов.");

        await using var conn = await connections.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var existingMembership = await conn.ExecuteScalarAsync<long?>(new CommandDefinition(
            "SELECT clan_id FROM meta_clan_members WHERE chat_id = @chatId AND user_id = @userId LIMIT 1",
            new { chatId, userId },
            transaction: tx,
            cancellationToken: ct));
        if (existingMembership is not null)
        {
            await tx.CommitAsync(ct);
            return new ClanCreateResult(false, "Ты уже состоишь в клане этого чата.");
        }

        var tagExists = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM meta_clans WHERE chat_id = @chatId AND lower(tag) = lower(@tag)",
            new { chatId, tag },
            transaction: tx,
            cancellationToken: ct));
        if (tagExists > 0)
        {
            await tx.CommitAsync(ct);
            return new ClanCreateResult(false, "Клан с таким тегом уже существует.");
        }

        const string insertClanSql = """
            INSERT INTO meta_clans (chat_id, name, tag, owner_user_id)
            VALUES (@chatId, @name, @tag, @userId)
            RETURNING id
            """;
        var clanId = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            insertClanSql,
            new { chatId, name, tag, userId },
            transaction: tx,
            cancellationToken: ct));

        const string insertMemberSql = """
            INSERT INTO meta_clan_members (clan_id, chat_id, user_id, display_name, role)
            VALUES (@clanId, @chatId, @userId, @displayName, 'owner')
            """;
        await conn.ExecuteAsync(new CommandDefinition(
            insertMemberSql,
            new { clanId, chatId, userId, displayName },
            transaction: tx,
            cancellationToken: ct));

        await EnsureSeasonClanAsync(conn, tx, season.Id, chatId, clanId, ct);
        await tx.CommitAsync(ct);

        var clan = await GetClanByTagAsync(season, chatId, tag, ct);
        return new ClanCreateResult(true, "Клан создан.", clan);
    }

    public async Task<ClanJoinResult> JoinAsync(
        MetaSeason season,
        long chatId,
        long userId,
        string displayName,
        string tag,
        CancellationToken ct)
    {
        tag = NormalizeTag(tag);
        await using var conn = await connections.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var existingMembership = await conn.ExecuteScalarAsync<long?>(new CommandDefinition(
            "SELECT clan_id FROM meta_clan_members WHERE chat_id = @chatId AND user_id = @userId LIMIT 1",
            new { chatId, userId },
            transaction: tx,
            cancellationToken: ct));
        if (existingMembership is not null)
        {
            await tx.CommitAsync(ct);
            return new ClanJoinResult(false, "Ты уже состоишь в клане этого чата.");
        }

        var clanId = await conn.ExecuteScalarAsync<long?>(new CommandDefinition(
            "SELECT id FROM meta_clans WHERE chat_id = @chatId AND lower(tag) = lower(@tag) LIMIT 1",
            new { chatId, tag },
            transaction: tx,
            cancellationToken: ct));
        if (clanId is null)
        {
            await tx.CommitAsync(ct);
            return new ClanJoinResult(false, "Клан с таким тегом не найден.");
        }

        await conn.ExecuteAsync(new CommandDefinition(
            "INSERT INTO meta_clan_members (clan_id, chat_id, user_id, display_name, role) VALUES (@clanId, @chatId, @userId, @displayName, 'member')",
            new { clanId, chatId, userId, displayName },
            transaction: tx,
            cancellationToken: ct));

        await EnsureSeasonClanAsync(conn, tx, season.Id, chatId, clanId.Value, ct);
        await tx.CommitAsync(ct);

        var clan = await GetClanByTagAsync(season, chatId, tag, ct);
        return new ClanJoinResult(true, "Ты вступил в клан.", clan);
    }

    public async Task<ClanInfo?> GetUserClanAsync(MetaSeason season, long chatId, long userId, CancellationToken ct)
    {
        const string sql = """
            SELECT c.id,
                   c.chat_id AS ChatId,
                   c.name,
                   c.tag,
                   c.owner_user_id AS OwnerUserId,
                   c.created_at AS CreatedAt,
                   COUNT(m.user_id)::int AS MemberCount,
                   COALESCE(sc.xp, 0) AS SeasonXp,
                   COALESCE(sc.rating, 1000) AS SeasonRating
            FROM meta_clan_members me
            JOIN meta_clans c ON c.id = me.clan_id
            JOIN meta_clan_members m ON m.clan_id = c.id
            LEFT JOIN meta_season_clans sc ON sc.season_id = @seasonId AND sc.chat_id = @chatId AND sc.clan_id = c.id
            WHERE me.chat_id = @chatId AND me.user_id = @userId
            GROUP BY c.id, c.chat_id, c.name, c.tag, c.owner_user_id, c.created_at, sc.xp, sc.rating
            LIMIT 1
            """;
        await using var conn = await connections.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<ClanInfo>(new CommandDefinition(
            sql,
            new { seasonId = season.Id, chatId, userId },
            cancellationToken: ct));
    }

    public async Task<ClanInfo?> GetClanByTagAsync(MetaSeason season, long chatId, string tag, CancellationToken ct)
    {
        tag = NormalizeTag(tag);
        const string sql = """
            SELECT c.id,
                   c.chat_id AS ChatId,
                   c.name,
                   c.tag,
                   c.owner_user_id AS OwnerUserId,
                   c.created_at AS CreatedAt,
                   COUNT(m.user_id)::int AS MemberCount,
                   COALESCE(sc.xp, 0) AS SeasonXp,
                   COALESCE(sc.rating, 1000) AS SeasonRating
            FROM meta_clans c
            LEFT JOIN meta_clan_members m ON m.clan_id = c.id
            LEFT JOIN meta_season_clans sc ON sc.season_id = @seasonId AND sc.chat_id = @chatId AND sc.clan_id = c.id
            WHERE c.chat_id = @chatId AND lower(c.tag) = lower(@tag)
            GROUP BY c.id, c.chat_id, c.name, c.tag, c.owner_user_id, c.created_at, sc.xp, sc.rating
            LIMIT 1
            """;
        await using var conn = await connections.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<ClanInfo>(new CommandDefinition(
            sql,
            new { seasonId = season.Id, chatId, tag },
            cancellationToken: ct));
    }

    public async Task<IReadOnlyList<ClanMemberInfo>> GetMembersAsync(long clanId, CancellationToken ct)
    {
        const string sql = """
            SELECT clan_id AS ClanId,
                   user_id AS UserId,
                   display_name AS DisplayName,
                   role,
                   joined_at AS JoinedAt
            FROM meta_clan_members
            WHERE clan_id = @clanId
            ORDER BY role = 'owner' DESC, role = 'officer' DESC, joined_at ASC
            """;
        await using var conn = await connections.OpenAsync(ct);
        var rows = await conn.QueryAsync<ClanMemberInfo>(new CommandDefinition(sql, new { clanId }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<IReadOnlyList<ClanLeaderboardEntry>> GetTopAsync(MetaSeason season, long chatId, int limit, CancellationToken ct)
    {
        const string sql = """
            SELECT row_number() OVER (ORDER BY COALESCE(sc.xp, 0) DESC, COALESCE(sc.rating, 1000) DESC, c.id ASC)::int AS Place,
                   c.id AS ClanId,
                   c.name,
                   c.tag,
                   COUNT(m.user_id)::int AS Members,
                   COALESCE(sc.xp, 0) AS Xp,
                   COALESCE(sc.rating, 1000) AS Rating
            FROM meta_clans c
            LEFT JOIN meta_clan_members m ON m.clan_id = c.id
            LEFT JOIN meta_season_clans sc ON sc.season_id = @seasonId AND sc.chat_id = @chatId AND sc.clan_id = c.id
            WHERE c.chat_id = @chatId
            GROUP BY c.id, c.name, c.tag, sc.xp, sc.rating
            ORDER BY COALESCE(sc.xp, 0) DESC, COALESCE(sc.rating, 1000) DESC, c.id ASC
            LIMIT @limit
            """;
        await using var conn = await connections.OpenAsync(ct);
        var rows = await conn.QueryAsync<ClanLeaderboardEntry>(new CommandDefinition(
            sql,
            new { seasonId = season.Id, chatId, limit = Math.Clamp(limit, 1, 50) },
            cancellationToken: ct));
        return rows.ToList();
    }

    public async Task ApplyGameCompletedAsync(MetaSeason season, GameCompletedMetaEvent ev, long xpDelta, CancellationToken ct)
    {
        const string sql = """
            INSERT INTO meta_season_clans (season_id, chat_id, clan_id, xp, rating, wins, losses)
            SELECT @seasonId,
                   @chatId,
                   m.clan_id,
                   @xpDelta,
                   GREATEST(0, 1000 + @ratingDelta),
                   CASE WHEN @isWin THEN 1 ELSE 0 END,
                   CASE WHEN @isWin THEN 0 ELSE 1 END
            FROM meta_clan_members m
            WHERE m.chat_id = @chatId AND m.user_id = @userId
            ON CONFLICT (season_id, chat_id, clan_id)
            DO UPDATE SET xp = meta_season_clans.xp + @xpDelta,
                          rating = GREATEST(0, meta_season_clans.rating + @ratingDelta),
                          wins = meta_season_clans.wins + CASE WHEN @isWin THEN 1 ELSE 0 END,
                          losses = meta_season_clans.losses + CASE WHEN @isWin THEN 0 ELSE 1 END,
                          updated_at = now()
            """;
        await using var conn = await connections.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                seasonId = season.Id,
                chatId = ev.ChatId,
                userId = ev.UserId,
                xpDelta = Math.Max(1, xpDelta),
                ratingDelta = ev.IsWin ? 4 : -2,
                isWin = ev.IsWin,
            },
            cancellationToken: ct));
    }

    private static async Task EnsureSeasonClanAsync(
        System.Data.Common.DbConnection conn,
        System.Data.Common.DbTransaction tx,
        long seasonId,
        long chatId,
        long clanId,
        CancellationToken ct)
    {
        await conn.ExecuteAsync(new CommandDefinition(
            "INSERT INTO meta_season_clans (season_id, chat_id, clan_id) VALUES (@seasonId, @chatId, @clanId) ON CONFLICT DO NOTHING",
            new { seasonId, chatId, clanId },
            transaction: tx,
            cancellationToken: ct));
    }

    private static string NormalizeTag(string tag) => tag.Trim().TrimStart('#').ToUpperInvariant();

    private static bool IsValidTag(string tag) =>
        tag.Length is >= 2 and <= 12 && tag.All(ch => char.IsLetterOrDigit(ch) || ch is '_' or '-');
}

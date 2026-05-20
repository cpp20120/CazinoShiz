using BotFramework.Host;
using Dapper;

namespace Games.Meta;

public interface ITournamentStore
{
    Task<TournamentCreateResult> CreateAsync(
        MetaSeason season,
        long chatId,
        long createdBy,
        string gameKey,
        int entryFee,
        int maxPlayers,
        CancellationToken ct);

    Task<TournamentJoinResult> JoinAsync(
        long tournamentId,
        long userId,
        string displayName,
        CancellationToken ct);

    Task<TournamentInfo?> GetAsync(long tournamentId, CancellationToken ct);
    Task<IReadOnlyList<TournamentInfo>> GetOpenAsync(MetaSeason season, long chatId, int limit, CancellationToken ct);
    Task<IReadOnlyList<TournamentPlayerInfo>> GetPlayersAsync(long tournamentId, CancellationToken ct);
    Task<bool> StartAsync(long tournamentId, long userId, CancellationToken ct);
    Task<TournamentPlayerInfo?> FinishAsync(long tournamentId, long actorUserId, long winnerUserId, CancellationToken ct);
    Task<IReadOnlyList<TournamentPlayerInfo>?> CancelAsync(long tournamentId, long actorUserId, CancellationToken ct);
}

public sealed class TournamentStore(INpgsqlConnectionFactory connections) : ITournamentStore
{
    public async Task<TournamentCreateResult> CreateAsync(
        MetaSeason season,
        long chatId,
        long createdBy,
        string gameKey,
        int entryFee,
        int maxPlayers,
        CancellationToken ct)
    {
        gameKey = NormalizeGameKey(gameKey);
        if (!IsSupportedGame(gameKey))
            return new TournamentCreateResult(false, "Игра для турнира пока не поддерживается. Доступно: dice, cube, darts, football, basketball, bowling.");
        if (entryFee < 0)
            return new TournamentCreateResult(false, "Entry fee не может быть отрицательным.");
        if (maxPlayers is < 2 or > 64)
            return new TournamentCreateResult(false, "Количество игроков должно быть от 2 до 64.");

        const string sql = """
            INSERT INTO meta_tournaments (season_id, chat_id, game_key, type, status, entry_fee, max_players, created_by)
            VALUES (@seasonId, @chatId, @gameKey, 'single_elimination', 'open', @entryFee, @maxPlayers, @createdBy)
            RETURNING id
            """;

        await using var conn = await connections.OpenAsync(ct);
        var id = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            sql,
            new { seasonId = season.Id, chatId, gameKey, entryFee, maxPlayers, createdBy },
            cancellationToken: ct));

        var tournament = await GetAsync(id, ct);
        return new TournamentCreateResult(true, "Турнир создан.", tournament);
    }

    public async Task<TournamentJoinResult> JoinAsync(long tournamentId, long userId, string displayName, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var tournament = await GetForUpdateAsync(conn, tx, tournamentId, ct);
        if (tournament is null)
        {
            await tx.CommitAsync(ct);
            return new TournamentJoinResult(false, "Турнир не найден.");
        }
        if (tournament.Status != "open")
        {
            await tx.CommitAsync(ct);
            return new TournamentJoinResult(false, "Турнир уже не открыт для регистрации.");
        }
        if (tournament.PlayerCount >= tournament.MaxPlayers)
        {
            await tx.CommitAsync(ct);
            return new TournamentJoinResult(false, "Турнир уже заполнен.", tournament);
        }

        var exists = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM meta_tournament_players WHERE tournament_id = @tournamentId AND user_id = @userId",
            new { tournamentId, userId },
            transaction: tx,
            cancellationToken: ct));
        if (exists > 0)
        {
            await tx.CommitAsync(ct);
            return new TournamentJoinResult(false, "Ты уже зарегистрирован в этом турнире.", tournament);
        }

        await conn.ExecuteAsync(new CommandDefinition(
            "INSERT INTO meta_tournament_players (tournament_id, user_id, display_name) VALUES (@tournamentId, @userId, @displayName)",
            new { tournamentId, userId, displayName },
            transaction: tx,
            cancellationToken: ct));
        await tx.CommitAsync(ct);

        var updated = await GetAsync(tournamentId, ct);
        return new TournamentJoinResult(true, "Ты зарегистрирован в турнире.", updated);
    }

    public async Task<TournamentInfo?> GetAsync(long tournamentId, CancellationToken ct)
    {
        const string sql = """
            SELECT t.id,
                   t.season_id AS SeasonId,
                   t.chat_id AS ChatId,
                   t.game_key AS GameKey,
                   t.type,
                   t.status,
                   t.entry_fee AS EntryFee,
                   t.max_players AS MaxPlayers,
                   t.created_by AS CreatedBy,
                   t.created_at AS CreatedAt,
                   COUNT(p.user_id)::int AS PlayerCount,
                   (COUNT(p.user_id) * t.entry_fee)::bigint AS PrizePool
            FROM meta_tournaments t
            LEFT JOIN meta_tournament_players p ON p.tournament_id = t.id AND p.status IN ('joined', 'winner', 'eliminated')
            WHERE t.id = @tournamentId
            GROUP BY t.id, t.season_id, t.chat_id, t.game_key, t.type, t.status, t.entry_fee, t.max_players, t.created_by, t.created_at
            LIMIT 1
            """;
        await using var conn = await connections.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<TournamentInfo>(new CommandDefinition(sql, new { tournamentId }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<TournamentInfo>> GetOpenAsync(MetaSeason season, long chatId, int limit, CancellationToken ct)
    {
        const string sql = """
            SELECT t.id,
                   t.season_id AS SeasonId,
                   t.chat_id AS ChatId,
                   t.game_key AS GameKey,
                   t.type,
                   t.status,
                   t.entry_fee AS EntryFee,
                   t.max_players AS MaxPlayers,
                   t.created_by AS CreatedBy,
                   t.created_at AS CreatedAt,
                   COUNT(p.user_id)::int AS PlayerCount,
                   (COUNT(p.user_id) * t.entry_fee)::bigint AS PrizePool
            FROM meta_tournaments t
            LEFT JOIN meta_tournament_players p ON p.tournament_id = t.id AND p.status = 'joined'
            WHERE t.season_id = @seasonId AND t.chat_id = @chatId AND t.status = 'open'
            GROUP BY t.id, t.season_id, t.chat_id, t.game_key, t.type, t.status, t.entry_fee, t.max_players, t.created_by, t.created_at
            ORDER BY t.created_at DESC
            LIMIT @limit
            """;
        await using var conn = await connections.OpenAsync(ct);
        var rows = await conn.QueryAsync<TournamentInfo>(new CommandDefinition(
            sql,
            new { seasonId = season.Id, chatId, limit = Math.Clamp(limit, 1, 50) },
            cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<IReadOnlyList<TournamentPlayerInfo>> GetPlayersAsync(long tournamentId, CancellationToken ct)
    {
        const string sql = """
            SELECT tournament_id AS TournamentId,
                   user_id AS UserId,
                   display_name AS DisplayName,
                   status,
                   joined_at AS JoinedAt
            FROM meta_tournament_players
            WHERE tournament_id = @tournamentId
            ORDER BY joined_at ASC
            """;
        await using var conn = await connections.OpenAsync(ct);
        var rows = await conn.QueryAsync<TournamentPlayerInfo>(new CommandDefinition(sql, new { tournamentId }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<bool> StartAsync(long tournamentId, long userId, CancellationToken ct)
    {
        const string sql = """
            UPDATE meta_tournaments
            SET status = 'started', updated_at = now()
            WHERE id = @tournamentId
              AND created_by = @userId
              AND status = 'open'
              AND (SELECT COUNT(*) FROM meta_tournament_players WHERE tournament_id = @tournamentId AND status = 'joined') >= 2
            """;
        await using var conn = await connections.OpenAsync(ct);
        var changed = await conn.ExecuteAsync(new CommandDefinition(sql, new { tournamentId, userId }, cancellationToken: ct));
        return changed > 0;
    }

    public async Task<TournamentPlayerInfo?> FinishAsync(long tournamentId, long actorUserId, long winnerUserId, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var tournament = await GetForUpdateAsync(conn, tx, tournamentId, ct);
        if (tournament is null || tournament.CreatedBy != actorUserId || tournament.Status != "started")
        {
            await tx.CommitAsync(ct);
            return null;
        }

        var winner = await conn.QuerySingleOrDefaultAsync<TournamentPlayerInfo>(new CommandDefinition(
            """
            SELECT tournament_id AS TournamentId,
                   user_id AS UserId,
                   display_name AS DisplayName,
                   status,
                   joined_at AS JoinedAt
            FROM meta_tournament_players
            WHERE tournament_id = @tournamentId AND user_id = @winnerUserId AND status = 'joined'
            LIMIT 1
            """,
            new { tournamentId, winnerUserId },
            transaction: tx,
            cancellationToken: ct));
        if (winner is null)
        {
            await tx.CommitAsync(ct);
            return null;
        }

        await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE meta_tournament_players SET status = 'eliminated' WHERE tournament_id = @tournamentId AND status = 'joined' AND user_id <> @winnerUserId",
            new { tournamentId, winnerUserId },
            transaction: tx,
            cancellationToken: ct));
        await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE meta_tournament_players SET status = 'winner' WHERE tournament_id = @tournamentId AND user_id = @winnerUserId",
            new { tournamentId, winnerUserId },
            transaction: tx,
            cancellationToken: ct));
        await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE meta_tournaments SET status = 'finished', updated_at = now() WHERE id = @tournamentId",
            new { tournamentId },
            transaction: tx,
            cancellationToken: ct));

        await tx.CommitAsync(ct);
        return winner with { Status = "winner" };
    }

    public async Task<IReadOnlyList<TournamentPlayerInfo>?> CancelAsync(long tournamentId, long actorUserId, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var tournament = await GetForUpdateAsync(conn, tx, tournamentId, ct);
        if (tournament is null || tournament.CreatedBy != actorUserId || tournament.Status is not ("open" or "started"))
        {
            await tx.CommitAsync(ct);
            return null;
        }

        var players = (await conn.QueryAsync<TournamentPlayerInfo>(new CommandDefinition(
            """
            SELECT tournament_id AS TournamentId,
                   user_id AS UserId,
                   display_name AS DisplayName,
                   status,
                   joined_at AS JoinedAt
            FROM meta_tournament_players
            WHERE tournament_id = @tournamentId AND status = 'joined'
            ORDER BY joined_at ASC
            """,
            new { tournamentId },
            transaction: tx,
            cancellationToken: ct))).ToList();

        await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE meta_tournaments SET status = 'cancelled', updated_at = now() WHERE id = @tournamentId",
            new { tournamentId },
            transaction: tx,
            cancellationToken: ct));

        await tx.CommitAsync(ct);
        return players;
    }

    private async Task<TournamentInfo?> GetForUpdateAsync(
        System.Data.Common.DbConnection conn,
        System.Data.Common.DbTransaction tx,
        long tournamentId,
        CancellationToken ct)
    {
        const string sql = """
            SELECT t.id,
                   t.season_id AS SeasonId,
                   t.chat_id AS ChatId,
                   t.game_key AS GameKey,
                   t.type,
                   t.status,
                   t.entry_fee AS EntryFee,
                   t.max_players AS MaxPlayers,
                   t.created_by AS CreatedBy,
                   t.created_at AS CreatedAt,
                   COUNT(p.user_id)::int AS PlayerCount,
                   (COUNT(p.user_id) * t.entry_fee)::bigint AS PrizePool
            FROM meta_tournaments t
            LEFT JOIN meta_tournament_players p ON p.tournament_id = t.id AND p.status IN ('joined', 'winner', 'eliminated')
            WHERE t.id = @tournamentId
            GROUP BY t.id, t.season_id, t.chat_id, t.game_key, t.type, t.status, t.entry_fee, t.max_players, t.created_by, t.created_at
            FOR UPDATE OF t
            """;
        return await conn.QuerySingleOrDefaultAsync<TournamentInfo>(new CommandDefinition(
            sql,
            new { tournamentId },
            transaction: tx,
            cancellationToken: ct));
    }

    private static string NormalizeGameKey(string gameKey) => gameKey.Trim().TrimStart('/').ToLowerInvariant() switch
    {
        "cube" => "dicecube",
        var x => x,
    };

    private static bool IsSupportedGame(string gameKey) => gameKey is
        "dice" or "dicecube" or "darts" or "football" or "basketball" or "bowling";
}

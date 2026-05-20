using BotFramework.Host;
using Dapper;

namespace Games.Meta;

public interface ITournamentStore
{
    Task<TournamentCreateResult> CreateAsync(MetaSeason season, long chatId, long createdBy, string gameKey, int entryFee, int maxPlayers, CancellationToken ct);
    Task<TournamentJoinResult> JoinAsync(long tournamentId, long userId, string displayName, CancellationToken ct);
    Task<TournamentInfo?> GetAsync(long tournamentId, CancellationToken ct);
    Task<IReadOnlyList<TournamentInfo>> GetOpenAsync(MetaSeason season, long chatId, int limit, CancellationToken ct);
    Task<IReadOnlyList<TournamentPlayerInfo>> GetPlayersAsync(long tournamentId, CancellationToken ct);
    Task<IReadOnlyList<TournamentMatchInfo>> GetMatchesAsync(long tournamentId, CancellationToken ct);
    Task<bool> StartAsync(long tournamentId, long userId, CancellationToken ct);
    Task<TournamentReportResult> ReportMatchAsync(long matchId, long actorUserId, long victorUserId, CancellationToken ct);
    Task<TournamentPlayerInfo?> FinishAsync(long tournamentId, long actorUserId, long winnerUserId, CancellationToken ct);
    Task<IReadOnlyList<TournamentPlayerInfo>?> CancelAsync(long tournamentId, long actorUserId, CancellationToken ct);
}

public sealed class TournamentStore(INpgsqlConnectionFactory connections) : ITournamentStore
{\n    public async Task<TournamentCreateResult> CreateAsync(MetaSeason season, long chatId, long createdBy, string gameKey, int entryFee, int maxPlayers, CancellationToken ct)
    {
        gameKey = NormalizeGameKey(gameKey);
        if (!IsSupportedGame(gameKey))
            return new TournamentCreateResult(false, "Игра для турнира пока не поддерживается. Доступно: dice, cube, darts, football, basketball, bowling.");
        if (entryFee < 0) return new TournamentCreateResult(false, "Entry fee не может быть отрицательным.");
        if (maxPlayers is < 2 or > 64) return new TournamentCreateResult(false, "Количество игроков должно быть от 2 до 64.");

        const string sql = """
            INSERT INTO meta_tournaments (season_id, chat_id, game_key, type, status, entry_fee, max_players, created_by)
            VALUES (@seasonId, @chatId, @gameKey, 'single_elimination', 'open', @entryFee, @maxPlayers, @createdBy)
            RETURNING id
            """;
        await using var conn = await connections.OpenAsync(ct);
        var id = await conn.ExecuteScalarAsync<long>(new CommandDefinition(sql, new { seasonId = season.Id, chatId, gameKey, entryFee, maxPlayers, createdBy }, cancellationToken: ct));
        var tournament = await GetAsync(id, ct);
        return new TournamentCreateResult(true, "Турнир создан.", tournament);
    }

    public async Task<TournamentJoinResult> JoinAsync(long tournamentId, long userId, string displayName, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var tournament = await GetForUpdateAsync(conn, tx, tournamentId, ct);
        if (tournament is null) { await tx.CommitAsync(ct); return new TournamentJoinResult(false, "Турнир не найден."); }
        if (tournament.Status != "open") { await tx.CommitAsync(ct); return new TournamentJoinResult(false, "Турнир уже не открыт для регистрации."); }
        if (tournament.PlayerCount >= tournament.MaxPlayers) { await tx.CommitAsync(ct); return new TournamentJoinResult(false, "Турнир уже заполнен.", tournament); }

        var exists = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM meta_tournament_players WHERE tournament_id = @tournamentId AND user_id = @userId",
            new { tournamentId, userId }, transaction: tx, cancellationToken: ct));
        if (exists > 0) { await tx.CommitAsync(ct); return new TournamentJoinResult(false, "Ты уже зарегистрирован в этом турнире.", tournament); }

        await conn.ExecuteAsync(new CommandDefinition(
            "INSERT INTO meta_tournament_players (tournament_id, user_id, display_name) VALUES (@tournamentId, @userId, @displayName)",
            new { tournamentId, userId, displayName }, transaction: tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        var updated = await GetAsync(tournamentId, ct);
        return new TournamentJoinResult(true, "Ты зарегистрирован в турнире.", updated);
    }

    public async Task<TournamentInfo?> GetAsync(long tournamentId, CancellationToken ct)
    {
        const string sql = """
            SELECT t.id, t.season_id AS SeasonId, t.chat_id AS ChatId, t.game_key AS GameKey, t.type, t.status,
                   t.entry_fee AS EntryFee, t.max_players AS MaxPlayers, t.created_by AS CreatedBy, t.created_at AS CreatedAt,
                   COUNT(p.user_id)::int AS PlayerCount, (COUNT(p.user_id) * t.entry_fee)::bigint AS PrizePool
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
            SELECT t.id, t.season_id AS SeasonId, t.chat_id AS ChatId, t.game_key AS GameKey, t.type, t.status,
                   t.entry_fee AS EntryFee, t.max_players AS MaxPlayers, t.created_by AS CreatedBy, t.created_at AS CreatedAt,
                   COUNT(p.user_id)::int AS PlayerCount, (COUNT(p.user_id) * t.entry_fee)::bigint AS PrizePool
            FROM meta_tournaments t
            LEFT JOIN meta_tournament_players p ON p.tournament_id = t.id AND p.status = 'joined'
            WHERE t.season_id = @seasonId AND t.chat_id = @chatId AND t.status = 'open'
            GROUP BY t.id, t.season_id, t.chat_id, t.game_key, t.type, t.status, t.entry_fee, t.max_players, t.created_by, t.created_at
            ORDER BY t.created_at DESC
            LIMIT @limit
            """;
        await using var conn = await connections.OpenAsync(ct);
        var rows = await conn.QueryAsync<TournamentInfo>(new CommandDefinition(sql, new { seasonId = season.Id, chatId, limit = Math.Clamp(limit, 1, 50) }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<IReadOnlyList<TournamentPlayerInfo>> GetPlayersAsync(long tournamentId, CancellationToken ct)
    {
        const string sql = """
            SELECT tournament_id AS TournamentId, user_id AS UserId, display_name AS DisplayName, status, joined_at AS JoinedAt
            FROM meta_tournament_players
            WHERE tournament_id = @tournamentId
            ORDER BY joined_at ASC
            """;
        await using var conn = await connections.OpenAsync(ct);
        var rows = await conn.QueryAsync<TournamentPlayerInfo>(new CommandDefinition(sql, new { tournamentId }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<IReadOnlyList<TournamentMatchInfo>> GetMatchesAsync(long tournamentId, CancellationToken ct)
    {
        const string sql = """
            SELECT id AS Id, tournament_id AS TournamentId, round AS Round, match_index AS MatchIndex, status AS Status,
                   player1_user_id AS Player1UserId, player1_display_name AS Player1DisplayName,
                   player2_user_id AS Player2UserId, player2_display_name AS Player2DisplayName,
                   victor_user_id AS VictorUserId, created_at AS CreatedAt, updated_at AS UpdatedAt
            FROM meta_tournament_matches
            WHERE tournament_id = @tournamentId
            ORDER BY round ASC, match_index ASC
            """;
        await using var conn = await connections.OpenAsync(ct);
        var rows = await conn.QueryAsync<TournamentMatchInfo>(new CommandDefinition(sql, new { tournamentId }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<bool> StartAsync(long tournamentId, long userId, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var tournament = await GetForUpdateAsync(conn, tx, tournamentId, ct);
        if (tournament is null || tournament.CreatedBy != userId || tournament.Status != "open") { await tx.CommitAsync(ct); return false; }

        var existing = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM meta_tournament_matches WHERE tournament_id = @tournamentId",
            new { tournamentId }, transaction: tx, cancellationToken: ct));
        if (existing > 0) { await tx.CommitAsync(ct); return false; }

        var players = (await conn.QueryAsync<TournamentPlayerInfo>(new CommandDefinition(
            """
            SELECT tournament_id AS TournamentId, user_id AS UserId, display_name AS DisplayName, status, joined_at AS JoinedAt
            FROM meta_tournament_players
            WHERE tournament_id = @tournamentId AND status = 'joined'
            ORDER BY joined_at ASC
            """, new { tournamentId }, transaction: tx, cancellationToken: ct))).ToList();
        if (players.Count < 2) { await tx.CommitAsync(ct); return false; }

        await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE meta_tournaments SET status = 'started', updated_at = now() WHERE id = @tournamentId",
            new { tournamentId }, transaction: tx, cancellationToken: ct));

        var size = NextPowerOfTwo(players.Count);
        var rounds = (int)Math.Log2(size);
        for (var round = 1; round <= rounds; round++)
        {
            var matchCount = size / (int)Math.Pow(2, round);
            for (var index = 1; index <= matchCount; index++)
            {
                await conn.ExecuteAsync(new CommandDefinition(
                    "INSERT INTO meta_tournament_matches (tournament_id, round, match_index, status) VALUES (@tournamentId, @round, @index, 'pending')",
                    new { tournamentId, round, index }, transaction: tx, cancellationToken: ct));
            }
        }

        for (var i = 0; i < size; i += 2)
        {
            var p1 = i < players.Count ? players[i] : null;
            var p2 = i + 1 < players.Count ? players[i + 1] : null;
            var index = i / 2 + 1;
            if (p1 is not null && p2 is not null)
            {
                await conn.ExecuteAsync(new CommandDefinition(
                    """
                    UPDATE meta_tournament_matches
                    SET status = 'ready', player1_user_id = @p1id, player1_display_name = @p1name,
                        player2_user_id = @p2id, player2_display_name = @p2name, updated_at = now()
                    WHERE tournament_id = @tournamentId AND round = 1 AND match_index = @index
                    """, new { tournamentId, index, p1id = p1.UserId, p1name = p1.DisplayName, p2id = p2.UserId, p2name = p2.DisplayName }, transaction: tx, cancellationToken: ct));
            }
            else if (p1 is not null)
            {
                await conn.ExecuteAsync(new CommandDefinition(
                    """
                    UPDATE meta_tournament_matches
                    SET status = 'byed', player1_user_id = @p1id, player1_display_name = @p1name, victor_user_id = @p1id, updated_at = now()
                    WHERE tournament_id = @tournamentId AND round = 1 AND match_index = @index
                    """, new { tournamentId, index, p1id = p1.UserId, p1name = p1.DisplayName }, transaction: tx, cancellationToken: ct));
                if (rounds == 1) await CompleteTournamentAsync(conn, tx, tournamentId, p1.UserId, ct);
                else await AdvanceAsync(conn, tx, tournamentId, 1, index, p1.UserId, p1.DisplayName, ct);
            }
        }

        await tx.CommitAsync(ct);
        return true;
    }

    public async Task<TournamentReportResult> ReportMatchAsync(long matchId, long actorUserId, long victorUserId, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var match = await GetMatchForUpdateAsync(conn, tx, matchId, ct);
        if (match is null) { await tx.CommitAsync(ct); return new TournamentReportResult(false, false, "Матч не найден."); }
        var tournament = await GetForUpdateAsync(conn, tx, match.TournamentId, ct);
        if (tournament is null || tournament.CreatedBy != actorUserId || tournament.Status != "started")
        {
            await tx.CommitAsync(ct);
            return new TournamentReportResult(false, false, "Нужен creator и started-турнир.");
        }
        if (match.Status != "ready" || match.Player1UserId is null || match.Player2UserId is null)
        {
            await tx.CommitAsync(ct);
            return new TournamentReportResult(false, false, "Матч не готов к репорту.");
        }
        if (victorUserId != match.Player1UserId && victorUserId != match.Player2UserId)
        {
            await tx.CommitAsync(ct);
            return new TournamentReportResult(false, false, "Игрок не участвует в этом матче.");
        }

        var victorName = victorUserId == match.Player1UserId ? match.Player1DisplayName! : match.Player2DisplayName!;
        await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE meta_tournament_matches SET status = 'finished', victor_user_id = @victorUserId, updated_at = now() WHERE id = @matchId",
            new { matchId, victorUserId }, transaction: tx, cancellationToken: ct));

        var maxRound = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT max(round)::int FROM meta_tournament_matches WHERE tournament_id = @tournamentId",
            new { tournamentId = match.TournamentId }, transaction: tx, cancellationToken: ct));
        var finished = match.Round >= maxRound;
        if (finished)
        {
            await CompleteTournamentAsync(conn, tx, match.TournamentId, victorUserId, ct);
        }
        else
        {
            await AdvanceAsync(conn, tx, match.TournamentId, match.Round, match.MatchIndex, victorUserId, victorName, ct);
        }

        await tx.CommitAsync(ct);
        var updated = await GetMatchAsync(matchId, ct);
        var victor = await GetPlayerAsync(match.TournamentId, victorUserId, ct);
        return new TournamentReportResult(true, finished, finished ? "Турнир завершён." : "Матч засчитан, игрок продвинут дальше.", updated, victor);
    }

    public async Task<TournamentPlayerInfo?> FinishAsync(long tournamentId, long actorUserId, long winnerUserId, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var tournament = await GetForUpdateAsync(conn, tx, tournamentId, ct);
        if (tournament is null || tournament.CreatedBy != actorUserId || tournament.Status != "started") { await tx.CommitAsync(ct); return null; }
        var player = await GetPlayerForUpdateAsync(conn, tx, tournamentId, winnerUserId, ct);
        if (player is null || player.Status != "joined") { await tx.CommitAsync(ct); return null; }
        await CompleteTournamentAsync(conn, tx, tournamentId, winnerUserId, ct);
        await tx.CommitAsync(ct);
        return player with { Status = "winner" };
    }

    public async Task<IReadOnlyList<TournamentPlayerInfo>?> CancelAsync(long tournamentId, long actorUserId, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var tournament = await GetForUpdateAsync(conn, tx, tournamentId, ct);
        if (tournament is null || tournament.CreatedBy != actorUserId || tournament.Status is not ("open" or "started")) { await tx.CommitAsync(ct); return null; }
        var players = (await conn.QueryAsync<TournamentPlayerInfo>(new CommandDefinition(
            """
            SELECT tournament_id AS TournamentId, user_id AS UserId, display_name AS DisplayName, status, joined_at AS JoinedAt
            FROM meta_tournament_players
            WHERE tournament_id = @tournamentId AND status = 'joined'
            ORDER BY joined_at ASC
            """, new { tournamentId }, transaction: tx, cancellationToken: ct))).ToList();
        await conn.ExecuteAsync(new CommandDefinition("UPDATE meta_tournaments SET status = 'cancelled', updated_at = now() WHERE id = @tournamentId", new { tournamentId }, transaction: tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return players;
    }

    private async Task AdvanceAsync(System.Data.Common.DbConnection conn, System.Data.Common.DbTransaction tx, long tournamentId, int round, int matchIndex, long userId, string displayName, CancellationToken ct)
    {
        var nextRound = round + 1;
        var nextIndex = (matchIndex + 1) / 2;
        var firstSlot = matchIndex % 2 == 1;
        var slotSql = firstSlot
            ? "player1_user_id = @userId, player1_display_name = @displayName"
            : "player2_user_id = @userId, player2_display_name = @displayName";
        await conn.ExecuteAsync(new CommandDefinition(
            $"UPDATE meta_tournament_matches SET {slotSql}, updated_at = now() WHERE tournament_id = @tournamentId AND round = @nextRound AND match_index = @nextIndex",
            new { tournamentId, nextRound, nextIndex, userId, displayName }, transaction: tx, cancellationToken: ct));
        await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE meta_tournament_matches
            SET status = 'ready', updated_at = now()
            WHERE tournament_id = @tournamentId AND round = @nextRound AND match_index = @nextIndex
              AND player1_user_id IS NOT NULL AND player2_user_id IS NOT NULL AND status = 'pending'
            """, new { tournamentId, nextRound, nextIndex }, transaction: tx, cancellationToken: ct));
    }

    private async Task CompleteTournamentAsync(System.Data.Common.DbConnection conn, System.Data.Common.DbTransaction tx, long tournamentId, long victorUserId, CancellationToken ct)
    {
        await conn.ExecuteAsync(new CommandDefinition("UPDATE meta_tournament_players SET status = 'eliminated' WHERE tournament_id = @tournamentId AND status = 'joined' AND user_id <> @victorUserId", new { tournamentId, victorUserId }, transaction: tx, cancellationToken: ct));
        await conn.ExecuteAsync(new CommandDefinition("UPDATE meta_tournament_players SET status = 'winner' WHERE tournament_id = @tournamentId AND user_id = @victorUserId", new { tournamentId, victorUserId }, transaction: tx, cancellationToken: ct));
        await conn.ExecuteAsync(new CommandDefinition("UPDATE meta_tournaments SET status = 'finished', updated_at = now() WHERE id = @tournamentId", new { tournamentId }, transaction: tx, cancellationToken: ct));
    }

    private async Task<TournamentMatchInfo?> GetMatchAsync(long matchId, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<TournamentMatchInfo>(new CommandDefinition(MatchSelectSql + " WHERE id = @matchId", new { matchId }, cancellationToken: ct));
    }

    private static Task<TournamentMatchInfo?> GetMatchForUpdateAsync(System.Data.Common.DbConnection conn, System.Data.Common.DbTransaction tx, long matchId, CancellationToken ct) =>
        conn.QuerySingleOrDefaultAsync<TournamentMatchInfo>(new CommandDefinition(MatchSelectSql + " WHERE id = @matchId FOR UPDATE", new { matchId }, transaction: tx, cancellationToken: ct));

    private async Task<TournamentPlayerInfo?> GetPlayerAsync(long tournamentId, long userId, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<TournamentPlayerInfo>(new CommandDefinition(PlayerSelectSql + " WHERE tournament_id = @tournamentId AND user_id = @userId LIMIT 1", new { tournamentId, userId }, cancellationToken: ct));
    }

    private static Task<TournamentPlayerInfo?> GetPlayerForUpdateAsync(System.Data.Common.DbConnection conn, System.Data.Common.DbTransaction tx, long tournamentId, long userId, CancellationToken ct) =>
        conn.QuerySingleOrDefaultAsync<TournamentPlayerInfo>(new CommandDefinition(PlayerSelectSql + " WHERE tournament_id = @tournamentId AND user_id = @userId LIMIT 1 FOR UPDATE", new { tournamentId, userId }, transaction: tx, cancellationToken: ct));

    private async Task<TournamentInfo?> GetForUpdateAsync(System.Data.Common.DbConnection conn, System.Data.Common.DbTransaction tx, long tournamentId, CancellationToken ct)
    {
        const string sql = """
            SELECT t.id, t.season_id AS SeasonId, t.chat_id AS ChatId, t.game_key AS GameKey, t.type, t.status,
                   t.entry_fee AS EntryFee, t.max_players AS MaxPlayers, t.created_by AS CreatedBy, t.created_at AS CreatedAt,
                   COUNT(p.user_id)::int AS PlayerCount, (COUNT(p.user_id) * t.entry_fee)::bigint AS PrizePool
            FROM meta_tournaments t
            LEFT JOIN meta_tournament_players p ON p.tournament_id = t.id AND p.status IN ('joined', 'winner', 'eliminated')
            WHERE t.id = @tournamentId
            GROUP BY t.id, t.season_id, t.chat_id, t.game_key, t.type, t.status, t.entry_fee, t.max_players, t.created_by, t.created_at
            FOR UPDATE OF t
            """;
        return await conn.QuerySingleOrDefaultAsync<TournamentInfo>(new CommandDefinition(sql, new { tournamentId }, transaction: tx, cancellationToken: ct));
    }

    private static int NextPowerOfTwo(int value)
    {
        var result = 1;
        while (result < value) result <<= 1;
        return result;
    }

    private static string NormalizeGameKey(string gameKey) => gameKey.Trim().TrimStart('/').ToLowerInvariant() switch { "cube" => "dicecube", var x => x };
    private static bool IsSupportedGame(string gameKey) => gameKey is "dice" or "dicecube" or "darts" or "football" or "basketball" or "bowling";

    private const string MatchSelectSql = """
        SELECT id AS Id, tournament_id AS TournamentId, round AS Round, match_index AS MatchIndex, status AS Status,
               player1_user_id AS Player1UserId, player1_display_name AS Player1DisplayName,
               player2_user_id AS Player2UserId, player2_display_name AS Player2DisplayName,
               victor_user_id AS VictorUserId, created_at AS CreatedAt, updated_at AS UpdatedAt
        FROM meta_tournament_matches
        """;

    private const string PlayerSelectSql = """
        SELECT tournament_id AS TournamentId, user_id AS UserId, display_name AS DisplayName, status, joined_at AS JoinedAt
        FROM meta_tournament_players
        """;
}

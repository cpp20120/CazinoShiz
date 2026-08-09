using Dapper;

namespace Games.Meta.Infrastructure.Persistence;

public sealed class MetaStore(
    INpgsqlConnectionFactory connections,
    IRuntimeTuningAccessor tuning) : IMetaStore
{
    public async Task<MetaSeason> GetOrCreateActiveSeasonAsync(CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        const string findSql = """
            SELECT id,
                   name,
                   starts_at AS StartsAt,
                   ends_at AS EndsAt,
                   status,
                   config::text AS ConfigJson
            FROM meta_seasons
            WHERE status = 'active'
              AND starts_at <= now()
              AND ends_at > now()
            ORDER BY starts_at DESC
            LIMIT 1
            """;

        // Normal reads must remain lock-free. The scope lock is needed only
        // while an active season is absent, when several requests could
        // otherwise create it concurrently.
        var existing = (await conn.QuerySingleOrDefaultAsync<MetaSeasonRow>(new CommandDefinition(
            findSql, cancellationToken: ct)))?.ToDomain();

        if (existing is not null)
            return existing;

        await using var tx = await conn.BeginTransactionAsync(ct);

        // Lock the already-provisioned tenant_scopes row only for the cold
        // read-or-create path. Different scopes continue independently and
        // PostgreSQL exposes this lock through its normal diagnostics.
        var scopeLock = await conn.QuerySingleOrDefaultAsync<long?>(new CommandDefinition(
            """
            SELECT scope_key
            FROM tenant_scopes
            WHERE tenant_key = casinoshiz_current_tenant_key()
              AND scope_key = casinoshiz_current_scope_key()
            FOR UPDATE
            """,
            transaction: tx,
            cancellationToken: ct));

        // Direct store tests and legacy background calls are intentionally
        // unbound. Preserve their read-or-create serialization without putting
        // any tenant-bound production request behind this fallback lock.
        if (scopeLock is null)
        {
            await conn.ExecuteAsync(new CommandDefinition(
                "SELECT pg_advisory_xact_lock(hashtext('casinoshiz.meta.active-season.unbound'))",
                transaction: tx,
                cancellationToken: ct));
        }

        await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE meta_seasons
            SET status = 'finished',
                updated_at = now()
            WHERE status = 'active'
              AND ends_at <= now()
            """,
            transaction: tx,
            cancellationToken: ct));

        // Another request may have created the season while this request was
        // waiting for the per-scope lock, so read again inside the transaction.
        existing = (await conn.QuerySingleOrDefaultAsync<MetaSeasonRow>(new CommandDefinition(
            findSql, transaction: tx, cancellationToken: ct)))?.ToDomain();

        if (existing is not null)
        {
            await EnsurePreparedSeasonsAsync(conn, tx, ct);
            await tx.CommitAsync(ct);
            return existing;
        }

        const string activateSql = """
            UPDATE meta_seasons
            SET status = 'active',
                updated_at = now()
            WHERE id = (
                SELECT id
                FROM meta_seasons
                WHERE status = 'planned'
                  AND starts_at <= now()
                  AND ends_at > now()
                ORDER BY starts_at ASC, id ASC
                LIMIT 1
                FOR UPDATE
            )
            RETURNING id,
                      name,
                      starts_at AS StartsAt,
                      ends_at AS EndsAt,
                      status,
                      config::text AS ConfigJson
            """;

        var activated = (await conn.QuerySingleOrDefaultAsync<MetaSeasonRow>(new CommandDefinition(
            activateSql,
            transaction: tx,
            cancellationToken: ct)))?.ToDomain();

        if (activated is not null)
        {
            await EnsurePreparedSeasonsAsync(conn, tx, ct);
            await tx.CommitAsync(ct);
            return activated;
        }

        var seasonNumber = await NextSeasonNumberAsync(conn, tx, ct);
        var startsAtUtc = await conn.ExecuteScalarAsync<DateTime>(new CommandDefinition(
            "SELECT date_trunc('day', now())",
            transaction: tx,
            cancellationToken: ct));
        var startsAt = new DateTimeOffset(DateTime.SpecifyKind(startsAtUtc, DateTimeKind.Utc));
        var endsAt = startsAt.AddDays(SeasonPlanFactory.DefaultDurationDays);

        const string insertSql = """
            INSERT INTO meta_seasons (name, starts_at, ends_at, status, config)
            VALUES (
                @name,
                @startsAt,
                @endsAt,
                'active',
                CAST(@configJson AS jsonb)
            )
            RETURNING id,
                      name,
                      starts_at AS StartsAt,
                      ends_at AS EndsAt,
                      status,
                      config::text AS ConfigJson
            """;

        var created = (await conn.QuerySingleAsync<MetaSeasonRow>(new CommandDefinition(
            insertSql,
            new
            {
                name = SeasonPlanFactory.NameFor(seasonNumber),
                startsAt,
                endsAt,
                configJson = SeasonPlanFactory.BuildConfigJson(seasonNumber),
            },
            transaction: tx,
            cancellationToken: ct))).ToDomain();

        await EnsurePreparedSeasonsAsync(conn, tx, ct);
        await tx.CommitAsync(ct);
        return created;
    }

    public async Task<SeasonPlayer> EnsurePlayerAsync(
        MetaSeason season,
        long chatId,
        long userId,
        string displayName,
        CancellationToken ct)
    {
        var progression = SeasonProgressionConfig.FromSeason(season);
        const string sql = """
            INSERT INTO meta_season_players (season_id, chat_id, user_id, display_name, rating)
            VALUES (@seasonId, @chatId, @userId, @displayName, @ratingStart)
            ON CONFLICT (season_id, chat_id, user_id)
            DO UPDATE SET display_name = EXCLUDED.display_name,
                          updated_at = now()
            RETURNING season_id AS SeasonId,
                      chat_id AS ChatId,
                      user_id AS UserId,
                      display_name AS DisplayName,
                      xp,
                      level,
                      rating,
                      games_played AS GamesPlayed,
                      wins,
                      losses,
                      total_staked AS TotalStaked,
                      total_payout AS TotalPayout,
                      updated_at AS UpdatedAt
            """;

        await using var conn = await connections.OpenAsync(ct);
        return await conn.QuerySingleAsync<SeasonPlayer>(new CommandDefinition(
            sql,
            new { seasonId = season.Id, chatId, userId, displayName, ratingStart = progression.RatingStart },
            cancellationToken: ct));
    }

    public async Task<SeasonPlayer> ApplyGameCompletedAsync(
        long chatId,
        long userId,
        string displayName,
        long stake,
        long payout,
        bool isWin,
        CancellationToken ct)
    {
        var season = await GetOrCreateActiveSeasonAsync(ct);
        var progression = SeasonProgressionConfig.FromSeason(season);
        var xpDelta = progression.CalculateXpDelta(stake, isWin);
        var ratingDelta = progression.CalculateRatingDelta(isWin);

        const string sql = """
            INSERT INTO meta_season_players (
                season_id,
                chat_id,
                user_id,
                display_name,
                xp,
                level,
                rating,
                games_played,
                wins,
                losses,
                total_staked,
                total_payout
            )
            VALUES (
                @seasonId,
                @chatId,
                @userId,
                @displayName,
                @xpDelta,
                @level,
                GREATEST(0, @ratingStart + @ratingDelta),
                1,
                CASE WHEN @isWin THEN 1 ELSE 0 END,
                CASE WHEN @isWin THEN 0 ELSE 1 END,
                @stake,
                @payout
            )
            ON CONFLICT (season_id, chat_id, user_id)
            DO UPDATE SET display_name = EXCLUDED.display_name,
                          xp = meta_season_players.xp + @xpDelta,
                          level = @level,
                          rating = GREATEST(0, meta_season_players.rating + @ratingDelta),
                          games_played = meta_season_players.games_played + 1,
                          wins = meta_season_players.wins + CASE WHEN @isWin THEN 1 ELSE 0 END,
                          losses = meta_season_players.losses + CASE WHEN @isWin THEN 0 ELSE 1 END,
                          total_staked = meta_season_players.total_staked + @stake,
                          total_payout = meta_season_players.total_payout + @payout,
                          updated_at = now()
            RETURNING season_id AS SeasonId,
                      chat_id AS ChatId,
                      user_id AS UserId,
                      display_name AS DisplayName,
                      xp,
                      level,
                      rating,
                      games_played AS GamesPlayed,
                      wins,
                      losses,
                      total_staked AS TotalStaked,
                      total_payout AS TotalPayout,
                      updated_at AS UpdatedAt
            """;

        await using var conn = await connections.OpenAsync(ct);
        var player = await conn.QuerySingleAsync<SeasonPlayer>(new CommandDefinition(
            sql,
            new
            {
                seasonId = season.Id,
                chatId,
                userId,
                displayName,
                xpDelta,
                level = progression.LevelForXp(xpDelta),
                ratingStart = progression.RatingStart,
                ratingDelta,
                isWin,
                stake = Math.Max(0, stake),
                payout = Math.Max(0, payout),
            },
            cancellationToken: ct));

        return await CorrectLevelAsync(conn, season.Id, chatId, userId, player, progression, ct);
    }

    public async Task<SeasonPlayer> AddSeasonXpAsync(
        long seasonId,
        long chatId,
        long userId,
        string displayName,
        long xpDelta,
        CancellationToken ct)
    {
        xpDelta = Math.Max(0, xpDelta);
        await using var conn = await connections.OpenAsync(ct);
        var progression = await GetSeasonProgressionAsync(conn, seasonId, ct);
        const string sql = """
            INSERT INTO meta_season_players (season_id, chat_id, user_id, display_name, xp, level)
            VALUES (@seasonId, @chatId, @userId, @displayName, @xpDelta, @level)
            ON CONFLICT (season_id, chat_id, user_id)
            DO UPDATE SET display_name = EXCLUDED.display_name,
                          xp = meta_season_players.xp + @xpDelta,
                          updated_at = now()
            RETURNING season_id AS SeasonId,
                      chat_id AS ChatId,
                      user_id AS UserId,
                      display_name AS DisplayName,
                      xp,
                      level,
                      rating,
                      games_played AS GamesPlayed,
                      wins,
                      losses,
                      total_staked AS TotalStaked,
                      total_payout AS TotalPayout,
                      updated_at AS UpdatedAt
            """;

        var player = await conn.QuerySingleAsync<SeasonPlayer>(new CommandDefinition(
            sql,
            new
            {
                seasonId,
                chatId,
                userId,
                displayName,
                xpDelta,
                level = progression.LevelForXp(xpDelta),
            },
            cancellationToken: ct));

        return await CorrectLevelAsync(conn, seasonId, chatId, userId, player, progression, ct);
    }

    public async Task<IReadOnlyList<AchievementUnlock>> UnlockAchievementsAsync(
        long seasonId,
        long chatId,
        long userId,
        IEnumerable<AchievementDefinition> achievements,
        CancellationToken ct)
    {
        var ids = achievements.Select(x => x.Id).Distinct(StringComparer.Ordinal).ToArray();
        if (ids.Length == 0) return [];

        const string sql = """
            INSERT INTO meta_player_achievements (achievement_id, season_id, chat_id, user_id)
            SELECT unnest(@ids), @seasonId, @chatId, @userId
            ON CONFLICT (achievement_id, season_id, chat_id, user_id) DO NOTHING
            RETURNING achievement_id AS AchievementId,
                      season_id AS SeasonId,
                      chat_id AS ChatId,
                      user_id AS UserId,
                      unlocked_at AS UnlockedAt
            """;

        await using var conn = await connections.OpenAsync(ct);
        var rows = await conn.QueryAsync<AchievementUnlock>(new CommandDefinition(
            sql,
            new { ids, seasonId, chatId, userId },
            cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<IReadOnlyList<PlayerAchievementView>> GetAchievementsAsync(long chatId, long userId, CancellationToken ct)
    {
        var season = await GetOrCreateActiveSeasonAsync(ct);
        const string sql = """
            SELECT achievement_id AS AchievementId,
                   unlocked_at AS UnlockedAt
            FROM meta_player_achievements
            WHERE season_id = @seasonId AND chat_id = @chatId AND user_id = @userId
            """;

        await using var conn = await connections.OpenAsync(ct);
        var unlocked = await conn.QueryAsync<(string AchievementId, DateTimeOffset UnlockedAt)>(new CommandDefinition(
            sql,
            new { seasonId = season.Id, chatId, userId },
            cancellationToken: ct));
        var map = unlocked.ToDictionary(x => x.AchievementId, x => x.UnlockedAt, StringComparer.Ordinal);

        var options = tuning.GetSection<MetaOptions>(MetaOptions.SectionName);
        return AchievementRegistry.GetAll(options.HighRollerTotalStaked, options.BigPayoutMinimum)
            .Select(x => new PlayerAchievementView(
                x.Id,
                x.IsSecret && !map.ContainsKey(x.Id) ? "???" : x.Title,
                x.IsSecret && !map.ContainsKey(x.Id) ? "Секретная ачивка." : x.Description,
                x.Category,
                map.ContainsKey(x.Id),
                map.GetValueOrDefault(x.Id)))
            .OrderByDescending(x => x.IsUnlocked)
            .ThenBy(x => x.Category, StringComparer.Ordinal)
            .ThenBy(x => x.Id, StringComparer.Ordinal)
            .ToList();
    }

    public async Task<GameStreakRecordResult?> RecordGamePlayedAsync(
        long seasonId,
        long chatId,
        long userId,
        string gameKey,
        DateOnly playedOn,
        CancellationToken ct)
    {
        if (!GameStreakRegistry.Supports(gameKey)) return null;

        const string advanceSql = """
            INSERT INTO meta_player_game_streaks (
                season_id,
                chat_id,
                user_id,
                game_key,
                current_streak,
                best_streak,
                total_play_days,
                last_played_on
            )
            VALUES (@seasonId, @chatId, @userId, @gameKey, 1, 1, 1, @playedOn)
            ON CONFLICT (season_id, chat_id, user_id, game_key)
            DO UPDATE SET
                current_streak = CASE
                    WHEN EXCLUDED.last_played_on = meta_player_game_streaks.last_played_on + 1
                        THEN meta_player_game_streaks.current_streak + 1
                    ELSE 1
                END,
                best_streak = GREATEST(
                    meta_player_game_streaks.best_streak,
                    CASE
                        WHEN EXCLUDED.last_played_on = meta_player_game_streaks.last_played_on + 1
                            THEN meta_player_game_streaks.current_streak + 1
                        ELSE 1
                    END
                ),
                total_play_days = meta_player_game_streaks.total_play_days + 1,
                last_played_on = EXCLUDED.last_played_on,
                updated_at = now()
            WHERE EXCLUDED.last_played_on > meta_player_game_streaks.last_played_on
            RETURNING season_id AS SeasonId,
                      chat_id AS ChatId,
                      user_id AS UserId,
                      game_key AS GameKey,
                      current_streak AS CurrentStreak,
                      best_streak AS BestStreak,
                      total_play_days AS TotalPlayDays,
                      last_played_on AS LastPlayedOn,
                      updated_at AS UpdatedAt,
                      true AS Advanced
            """;

        const string currentSql = """
            SELECT season_id AS SeasonId,
                   chat_id AS ChatId,
                   user_id AS UserId,
                   game_key AS GameKey,
                   current_streak AS CurrentStreak,
                   best_streak AS BestStreak,
                   total_play_days AS TotalPlayDays,
                   last_played_on AS LastPlayedOn,
                   updated_at AS UpdatedAt,
                   false AS Advanced
            FROM meta_player_game_streaks
            WHERE season_id = @seasonId
              AND chat_id = @chatId
              AND user_id = @userId
              AND game_key = @gameKey
            """;

        await using var conn = await connections.OpenAsync(ct);
        var args = new
        {
            seasonId,
            chatId,
            userId,
            gameKey,
            playedOn = playedOn.ToDateTime(TimeOnly.MinValue),
        };
        var row = await conn.QuerySingleOrDefaultAsync<GameStreakRecordRow>(new CommandDefinition(
            advanceSql,
            args,
            cancellationToken: ct));
        row ??= await conn.QuerySingleAsync<GameStreakRecordRow>(new CommandDefinition(
            currentSql,
            new { seasonId, chatId, userId, gameKey },
            cancellationToken: ct));
        return new GameStreakRecordResult(
            new GameStreak(
                row.SeasonId,
                row.ChatId,
                row.UserId,
                row.GameKey,
                row.CurrentStreak,
                row.BestStreak,
                row.TotalPlayDays,
                row.LastPlayedOn,
                row.UpdatedAt),
            row.Advanced);
    }

    private static async Task<SeasonProgressionConfig> GetSeasonProgressionAsync(
        System.Data.Common.DbConnection conn,
        long seasonId,
        CancellationToken ct)
    {
        const string sql = "SELECT config::text FROM meta_seasons WHERE id = @seasonId";
        var configJson = await conn.ExecuteScalarAsync<string?>(new CommandDefinition(
            sql,
            new { seasonId },
            cancellationToken: ct));
        return SeasonProgressionConfig.FromJson(configJson);
    }

    public async Task<IReadOnlyList<PlayerGameStreakView>> GetGameStreaksAsync(
        long chatId,
        long userId,
        CancellationToken ct)
    {
        var season = await GetOrCreateActiveSeasonAsync(ct);
        const string sql = """
            SELECT game_key AS GameKey,
                   current_streak AS CurrentStreak,
                   best_streak AS BestStreak,
                   total_play_days AS TotalPlayDays,
                   last_played_on AS LastPlayedOn
            FROM meta_player_game_streaks
            WHERE season_id = @seasonId AND chat_id = @chatId AND user_id = @userId
            """;

        await using var conn = await connections.OpenAsync(ct);
        var rows = await conn.QueryAsync<GameStreakRow>(new CommandDefinition(
            sql,
            new { seasonId = season.Id, chatId, userId },
            cancellationToken: ct));
        var map = rows.ToDictionary(x => x.GameKey, StringComparer.Ordinal);
        var options = tuning.GetSection<MetaOptions>(MetaOptions.SectionName);
        var now = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(
            Math.Clamp(options.StreakTimezoneOffsetHours, -14, 14)));
        var today = DateOnly.FromDateTime(now.DateTime);

        return GameStreakRegistry.Games.Select(game =>
        {
            map.TryGetValue(game.GameKey, out var row);
            return new PlayerGameStreakView(
                game.GameKey,
                game.Title,
                game.Command,
                row is null ? 0 : GameStreakRegistry.ActiveStreak(row.CurrentStreak, row.LastPlayedOn, today),
                row?.BestStreak ?? 0,
                row?.TotalPlayDays ?? 0,
                row?.LastPlayedOn);
        }).ToList();
    }

    public async Task<SeasonProfile> GetProfileAsync(
        long chatId,
        long userId,
        string displayName,
        CancellationToken ct)
    {
        var season = await GetOrCreateActiveSeasonAsync(ct);
        var progression = SeasonProgressionConfig.FromSeason(season);
        const string sql = """
            SELECT season_id AS SeasonId,
                   chat_id AS ChatId,
                   user_id AS UserId,
                   display_name AS DisplayName,
                   xp,
                   level,
                   rating,
                   games_played AS GamesPlayed,
                   wins,
                   losses,
                   total_staked AS TotalStaked,
                   total_payout AS TotalPayout,
                   updated_at AS UpdatedAt
            FROM meta_season_players
            WHERE season_id = @seasonId AND chat_id = @chatId AND user_id = @userId
            """;
        await using var conn = await connections.OpenAsync(ct);
        var stored = await conn.QuerySingleOrDefaultAsync<SeasonPlayer>(new CommandDefinition(
            sql,
            new { seasonId = season.Id, chatId, userId },
            cancellationToken: ct));
        var player = stored is null
            ? new SeasonPlayer(season.Id, chatId, userId, displayName, 0, 1, progression.RatingStart, 0, 0, 0, 0, 0, DateTimeOffset.UtcNow)
            : stored with { DisplayName = displayName };
        var floor = progression.XpForLevel(player.Level);
        var next = progression.XpForLevel(player.Level + 1);
        return new SeasonProfile(season, player, DivisionForRating(player.Rating), next, floor);
    }

    public async Task<IReadOnlyList<SeasonLeaderboardEntry>> GetTopAsync(long chatId, int limit, CancellationToken ct)
    {
        return await GetTopCoreAsync(chatId, Math.Clamp(limit, 1, 100), ct);
    }

    public async Task<IReadOnlyList<SeasonLeaderboardEntry>> GetTopSnapshotAsync(long chatId, CancellationToken ct)
    {
        return await GetTopCoreAsync(chatId, null, ct);
    }

    private async Task<IReadOnlyList<SeasonLeaderboardEntry>> GetTopCoreAsync(long chatId, int? limit, CancellationToken ct)
    {
        var season = await GetOrCreateActiveSeasonAsync(ct);
        const string sql = """
            SELECT row_number() OVER (ORDER BY xp DESC, rating DESC, user_id ASC)::int AS Place,
                   user_id AS UserId,
                   display_name AS DisplayName,
                   xp,
                   level,
                   rating,
                   games_played AS GamesPlayed,
                   wins,
                   losses
            FROM meta_season_players
            WHERE season_id = @seasonId AND chat_id = @chatId
            ORDER BY xp DESC, rating DESC, user_id ASC
            LIMIT COALESCE(@limit, 2147483647)
            """;

        await using var conn = await connections.OpenAsync(ct);
        var rows = await conn.QueryAsync<SeasonLeaderboardEntry>(new CommandDefinition(
            sql,
            new { seasonId = season.Id, chatId, limit },
            cancellationToken: ct));
        return rows.ToList();
    }

    private static async Task<SeasonPlayer> CorrectLevelAsync(
        System.Data.Common.DbConnection conn,
        long seasonId,
        long chatId,
        long userId,
        SeasonPlayer player,
        SeasonProgressionConfig progression,
        CancellationToken ct)
    {
        var correctedLevel = progression.LevelForXp(player.Xp);
        if (correctedLevel == player.Level)
            return player;

        const string levelSql = """
            UPDATE meta_season_players
            SET level = @level,
                updated_at = now()
            WHERE season_id = @seasonId AND chat_id = @chatId AND user_id = @userId
            RETURNING season_id AS SeasonId,
                      chat_id AS ChatId,
                      user_id AS UserId,
                      display_name AS DisplayName,
                      xp,
                      level,
                      rating,
                      games_played AS GamesPlayed,
                      wins,
                      losses,
                      total_staked AS TotalStaked,
                      total_payout AS TotalPayout,
                      updated_at AS UpdatedAt
            """;

        return await conn.QuerySingleAsync<SeasonPlayer>(new CommandDefinition(
            levelSql,
            new { level = correctedLevel, seasonId, chatId, userId },
            cancellationToken: ct));
    }

    private static async Task EnsurePreparedSeasonsAsync(
        System.Data.Common.DbConnection conn,
        System.Data.Common.DbTransaction tx,
        CancellationToken ct)
    {
        const string countSql = """
            SELECT count(*)::int
            FROM meta_seasons
            WHERE status = 'planned'
              AND ends_at > now()
            """;

        var existingPlanned = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            countSql,
            transaction: tx,
            cancellationToken: ct));
        var missing = SeasonPlanFactory.DefaultPreparedSeasonCount - existingPlanned;
        if (missing <= 0) return;

        var startsAt = await NextPlannedStartsAtAsync(conn, tx, ct);
        var startNumber = await NextSeasonNumberAsync(conn, tx, ct);
        var plans = SeasonPlanFactory.CreatePlans(
            startsAt,
            missing,
            SeasonPlanFactory.DefaultDurationDays,
            startNumber);

        const string insertSql = """
            INSERT INTO meta_seasons (name, starts_at, ends_at, status, config)
            VALUES (@name, @startsAt, @endsAt, 'planned', CAST(@configJson AS jsonb))
            """;

        foreach (var plan in plans)
        {
            await conn.ExecuteAsync(new CommandDefinition(
                insertSql,
                new
                {
                    name = plan.Name,
                    startsAt = plan.StartsAt,
                    endsAt = plan.EndsAt,
                    configJson = plan.ConfigJson,
                },
                transaction: tx,
                cancellationToken: ct));
        }
    }

    private static async Task<DateTimeOffset> NextPlannedStartsAtAsync(
        System.Data.Common.DbConnection conn,
        System.Data.Common.DbTransaction tx,
        CancellationToken ct)
    {
        const string sql = """
            SELECT COALESCE(max(ends_at), date_trunc('day', now()))
            FROM meta_seasons
            WHERE status IN ('active', 'planned')
            """;

        var startsAtUtc = await conn.ExecuteScalarAsync<DateTime>(new CommandDefinition(
            sql,
            transaction: tx,
            cancellationToken: ct));
        return new DateTimeOffset(DateTime.SpecifyKind(startsAtUtc, DateTimeKind.Utc));
    }

    private static async Task<int> NextSeasonNumberAsync(
        System.Data.Common.DbConnection conn,
        System.Data.Common.DbTransaction tx,
        CancellationToken ct)
    {
        const string sql = "SELECT count(*)::int + 1 FROM meta_seasons";
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            sql,
            transaction: tx,
            cancellationToken: ct));
    }

    private sealed record MetaSeasonRow(
        long Id,
        string Name,
        DateTime StartsAt,
        DateTime EndsAt,
        string Status,
        string ConfigJson)
    {
        public MetaSeason ToDomain() => new(
            Id,
            Name,
            new DateTimeOffset(DateTime.SpecifyKind(StartsAt, DateTimeKind.Utc)),
            new DateTimeOffset(DateTime.SpecifyKind(EndsAt, DateTimeKind.Utc)),
            Status,
            ConfigJson);
    }

    private static string DivisionForRating(int rating) => rating switch
    {
        < 900 => "Bronze",
        < 1100 => "Silver",
        < 1300 => "Gold",
        < 1500 => "Platinum",
        < 1800 => "Diamond",
        _ => "Shizoid",
    };
}

using BotFramework.Host;
using BotFramework.Host.Services;
using Dapper;

namespace Games.Meta;

public interface IMetaStore
{
    Task<MetaSeason> GetOrCreateActiveSeasonAsync(CancellationToken ct);
    Task<SeasonPlayer> EnsurePlayerAsync(MetaSeason season, long chatId, long userId, string displayName, CancellationToken ct);
    Task<SeasonPlayer> ApplyGameCompletedAsync(
        long chatId,
        long userId,
        string displayName,
        long stake,
        long payout,
        bool isWin,
        CancellationToken ct);
    Task<SeasonPlayer> AddSeasonXpAsync(
        long seasonId,
        long chatId,
        long userId,
        string displayName,
        long xpDelta,
        CancellationToken ct);
    Task<IReadOnlyList<AchievementUnlock>> UnlockAchievementsAsync(
        long seasonId,
        long chatId,
        long userId,
        IEnumerable<AchievementDefinition> achievements,
        CancellationToken ct);
    Task<IReadOnlyList<PlayerAchievementView>> GetAchievementsAsync(long chatId, long userId, CancellationToken ct);
    Task<GameStreakRecordResult?> RecordGamePlayedAsync(
        long seasonId,
        long chatId,
        long userId,
        string gameKey,
        DateOnly playedOn,
        CancellationToken ct);
    Task<IReadOnlyList<PlayerGameStreakView>> GetGameStreaksAsync(long chatId, long userId, CancellationToken ct);
    Task<SeasonProfile> GetProfileAsync(long chatId, long userId, string displayName, CancellationToken ct);
    Task<IReadOnlyList<SeasonLeaderboardEntry>> GetTopAsync(long chatId, int limit, CancellationToken ct);
}

public sealed class MetaStore(
    INpgsqlConnectionFactory connections,
    IRuntimeTuningAccessor tuning) : IMetaStore
{
    private const string DefaultSeasonConfigJson = """
        {
          "xp": {
            "play": 5,
            "win": 25,
            "loss": 2,
            "stakeMultiplier": 0.01,
            "maxXpPerGame": 500
          },
          "rating": {
            "enabled": true,
            "start": 1000,
            "winDelta": 16,
            "lossDelta": -12
          },
          "quests": {
            "dailyEnabled": true,
            "weeklyEnabled": true
          },
          "achievements": {
            "enabled": true
          },
          "clans": {
            "enabled": true,
            "maxMembers": 20
          },
          "tournaments": {
            "enabled": true,
            "maxActivePerChat": 3
          },
          "risk": {
            "enabled": true,
            "largeWinMultiplierAlert": 20,
            "suspiciousStreakThreshold": 12
          }
        }
        """;

    public async Task<MetaSeason> GetOrCreateActiveSeasonAsync(CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        const string findSql = """
            SELECT id,
                   name,
                   starts_at AS StartsAt,
                   ends_at AS EndsAt,
                   status,
                   config::text AS ConfigJson
            FROM meta_seasons
            WHERE status = 'active'
            ORDER BY starts_at DESC
            LIMIT 1
            """;

        var existing = await conn.QuerySingleOrDefaultAsync<MetaSeason>(new CommandDefinition(
            findSql, transaction: tx, cancellationToken: ct));

        if (existing is not null)
        {
            await tx.CommitAsync(ct);
            return existing;
        }

        const string insertSql = """
            INSERT INTO meta_seasons (name, starts_at, ends_at, status, config)
            VALUES (
                @name,
                date_trunc('day', now()),
                date_trunc('day', now()) + interval '30 days',
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

        var created = await conn.QuerySingleAsync<MetaSeason>(new CommandDefinition(
            insertSql,
            new { name = "Season 1: Shizoid League", configJson = DefaultSeasonConfigJson },
            transaction: tx,
            cancellationToken: ct));

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
        const string sql = """
            INSERT INTO meta_season_players (season_id, chat_id, user_id, display_name)
            VALUES (@seasonId, @chatId, @userId, @displayName)
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
            new { seasonId = season.Id, chatId, userId, displayName },
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
        var xpDelta = CalculateXpDelta(stake, isWin);
        var ratingDelta = isWin ? 16 : -12;

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
                GREATEST(0, 1000 + @ratingDelta),
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
                level = LevelForXp(xpDelta),
                ratingDelta,
                isWin,
                stake = Math.Max(0, stake),
                payout = Math.Max(0, payout),
            },
            cancellationToken: ct));

        return await CorrectLevelAsync(conn, season.Id, chatId, userId, player, ct);
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

        await using var conn = await connections.OpenAsync(ct);
        var player = await conn.QuerySingleAsync<SeasonPlayer>(new CommandDefinition(
            sql,
            new
            {
                seasonId,
                chatId,
                userId,
                displayName,
                xpDelta,
                level = LevelForXp(xpDelta),
            },
            cancellationToken: ct));

        return await CorrectLevelAsync(conn, seasonId, chatId, userId, player, ct);
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
            .ThenBy(x => x.Category)
            .ThenBy(x => x.Id)
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
        var args = new { seasonId, chatId, userId, gameKey, playedOn };
        var row = await conn.QuerySingleOrDefaultAsync<GameStreakRecordRow>(new CommandDefinition(
            advanceSql,
            args,
            cancellationToken: ct));
        row ??= await conn.QuerySingleAsync<GameStreakRecordRow>(new CommandDefinition(
            currentSql,
            new { seasonId, chatId, userId, gameKey, playedOn },
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
        var player = await EnsurePlayerAsync(season, chatId, userId, displayName, ct);
        var floor = XpForLevel(player.Level);
        var next = XpForLevel(player.Level + 1);
        return new SeasonProfile(season, player, DivisionForRating(player.Rating), next, floor);
    }

    public async Task<IReadOnlyList<SeasonLeaderboardEntry>> GetTopAsync(long chatId, int limit, CancellationToken ct)
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
            LIMIT @limit
            """;

        await using var conn = await connections.OpenAsync(ct);
        var rows = await conn.QueryAsync<SeasonLeaderboardEntry>(new CommandDefinition(
            sql,
            new { seasonId = season.Id, chatId, limit = Math.Clamp(limit, 1, 100) },
            cancellationToken: ct));
        return rows.ToList();
    }

    private async Task<SeasonPlayer> CorrectLevelAsync(
        System.Data.Common.DbConnection conn,
        long seasonId,
        long chatId,
        long userId,
        SeasonPlayer player,
        CancellationToken ct)
    {
        var correctedLevel = LevelForXp(player.Xp);
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

    private sealed record GameStreakRow(
        string GameKey,
        int CurrentStreak,
        int BestStreak,
        int TotalPlayDays,
        DateOnly LastPlayedOn);

    private sealed record GameStreakRecordRow(
        long SeasonId,
        long ChatId,
        long UserId,
        string GameKey,
        int CurrentStreak,
        int BestStreak,
        int TotalPlayDays,
        DateOnly LastPlayedOn,
        DateTimeOffset UpdatedAt,
        bool Advanced);

    private static long CalculateXpDelta(long stake, bool isWin)
    {
        var baseXp = isWin ? 25 : 2;
        var playXp = 5;
        var stakeXp = (long)Math.Floor(Math.Max(0, stake) * 0.01m);
        return Math.Clamp(playXp + baseXp + stakeXp, 1, 500);
    }

    private static int LevelForXp(long xp)
    {
        if (xp <= 0) return 1;
        return Math.Max(1, (int)Math.Floor(Math.Sqrt(xp / 100.0)) + 1);
    }

    private static long XpForLevel(int level)
    {
        var normalized = Math.Max(1, level) - 1;
        return 100L * normalized * normalized;
    }

    private static string DivisionForRating(int rating) => rating switch
    {
        < 900 => "Bronze",
        < 1100 => "Silver",
        < 1300 => "Gold",
        < 1500 => "Platinum",
        < 1800 => "Diamond",
        _ => "Shizoid"
    };
}

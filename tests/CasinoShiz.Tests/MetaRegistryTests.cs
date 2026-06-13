using BotFramework.Sdk;
using BotFramework.Host.Services;
using Games.Meta;
using System.Text.Json.Nodes;
using Xunit;

namespace CasinoShiz.Tests;

public sealed class MetaRegistryTests
{
    [Fact]
    public void MetaMigrations_HaveUniqueIds()
    {
        var ids = new MetaMigrations().Migrations.Select(x => x.Id).ToArray();

        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.Contains("008_meta_event_log", ids);
        Assert.Contains("009_game_streaks", ids);
    }

    [Fact]
    public void AchievementRegistry_AllIdsAreUnique()
    {
        var ids = AchievementRegistry.All.Select(x => x.Id).ToArray();
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void GameStreakRegistry_CreatesThreeAchievementsPerSupportedGame()
    {
        var achievements = GameStreakRegistry.GetAchievements();

        Assert.Equal(GameStreakRegistry.Games.Count * 3, achievements.Count);
        Assert.Equal(achievements.Count, achievements.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [InlineData(2, 0)]
    [InlineData(3, 1)]
    [InlineData(7, 2)]
    [InlineData(14, 3)]
    [InlineData(30, 3)]
    public void GameStreakRegistry_Evaluate_UnlocksReachedMilestones(int currentStreak, int expectedCount)
    {
        var streak = new GameStreak(
            1, 100, 42, MiniGameIds.Darts, currentStreak, currentStreak, currentStreak,
            new DateOnly(2026, 6, 13), DateTimeOffset.UtcNow);

        var unlocked = GameStreakRegistry.Evaluate(streak);

        Assert.Equal(expectedCount, unlocked.Count);
        Assert.All(unlocked, x => Assert.StartsWith("streak_darts_", x.Id));
    }

    [Fact]
    public void GameStreakRegistry_PlayDay_UsesConfiguredTimezone()
    {
        var occurredAt = new DateTimeOffset(2026, 6, 12, 20, 30, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();

        var playDay = GameStreakRegistry.PlayDay(occurredAt, 7);

        Assert.Equal(new DateOnly(2026, 6, 13), playDay);
    }

    [Theory]
    [InlineData(2026, 6, 13, 5)]
    [InlineData(2026, 6, 12, 5)]
    [InlineData(2026, 6, 11, 0)]
    public void GameStreakRegistry_ActiveStreak_ExpiresAfterMissedDay(
        int year,
        int month,
        int day,
        int expected)
    {
        var today = new DateOnly(2026, 6, 13);

        var active = GameStreakRegistry.ActiveStreak(5, new DateOnly(year, month, day), today);

        Assert.Equal(expected, active);
    }

    [Fact]
    public void QuestRegistry_AllIdsAreUnique()
    {
        var ids = QuestRegistry.All.Select(x => x.Id).ToArray();
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void AchievementRegistry_Evaluate_FirstWinLargePayout_ReturnsExpectedAchievements()
    {
        var ev = new GameCompletedMetaEvent(
            ChatId: 100,
            UserId: 42,
            DisplayName: "u",
            GameKey: MiniGameIds.Dice,
            Stake: 100,
            Payout: 1_000,
            IsWin: true,
            Multiplier: 10,
            OccurredAt: 1);
        var player = new SeasonPlayer(
            SeasonId: 1,
            ChatId: 100,
            UserId: 42,
            DisplayName: "u",
            Xp: 100,
            Level: 2,
            Rating: 1016,
            GamesPlayed: 1,
            Wins: 1,
            Losses: 0,
            TotalStaked: 100,
            TotalPayout: 1_000,
            UpdatedAt: DateTimeOffset.UtcNow);

        var ids = AchievementRegistry.Evaluate(ev, player).Select(x => x.Id).ToHashSet(StringComparer.Ordinal);

        Assert.Contains("first_game", ids);
        Assert.Contains("first_win", ids);
        Assert.Contains("big_payout", ids);
        Assert.Contains("dice_player", ids);
    }

    [Fact]
    public void AchievementRegistry_Evaluate_SeasonThresholds_ReturnsExpectedAchievements()
    {
        var ev = new GameCompletedMetaEvent(100, 42, "u", MiniGameIds.Darts, 50, 0, false, 0, 1);
        var player = new SeasonPlayer(
            SeasonId: 1,
            ChatId: 100,
            UserId: 42,
            DisplayName: "u",
            Xp: 500,
            Level: 3,
            Rating: 900,
            GamesPlayed: 50,
            Wins: 10,
            Losses: 40,
            TotalStaked: 10_000,
            TotalPayout: 2_000,
            UpdatedAt: DateTimeOffset.UtcNow);

        var ids = AchievementRegistry.Evaluate(ev, player).Select(x => x.Id).ToHashSet(StringComparer.Ordinal);

        Assert.Contains("ten_games", ids);
        Assert.Contains("fifty_games", ids);
        Assert.Contains("ten_wins", ids);
        Assert.Contains("high_roller", ids);
        Assert.Contains("darts_player", ids);
    }

    [Theory]
    [InlineData(999, 1_000, false)]
    [InlineData(1_000, 1_000, true)]
    [InlineData(4_999, 5_000, false)]
    [InlineData(5_000, 5_000, true)]
    public void AchievementRegistry_Evaluate_HighRollerUsesConfiguredThreshold(
        long totalStaked,
        long threshold,
        bool expected)
    {
        var ev = new GameCompletedMetaEvent(100, 42, "u", MiniGameIds.Dice, 10, 0, false, 0, 1);
        var player = new SeasonPlayer(
            SeasonId: 1,
            ChatId: 100,
            UserId: 42,
            DisplayName: "u",
            Xp: 10,
            Level: 1,
            Rating: 1000,
            GamesPlayed: 1,
            Wins: 0,
            Losses: 1,
            TotalStaked: totalStaked,
            TotalPayout: 0,
            UpdatedAt: DateTimeOffset.UtcNow);

        var unlocked = AchievementRegistry.Evaluate(ev, player, threshold);

        Assert.Equal(expected, unlocked.Any(x => x.Id == "high_roller"));
    }

    [Fact]
    public void AchievementRegistry_GetAll_HighRollerDescriptionUsesConfiguredThreshold()
    {
        var achievement = AchievementRegistry.GetAll(1_000).Single(x => x.Id == "high_roller");

        Assert.Contains("1 000", achievement.Description);
    }

    [Theory]
    [InlineData(999, 1_000, false)]
    [InlineData(1_000, 1_000, true)]
    [InlineData(4_999, 5_000, false)]
    [InlineData(5_000, 5_000, true)]
    public void AchievementRegistry_Evaluate_BigPayoutUsesConfiguredThreshold(
        long payout,
        long threshold,
        bool expected)
    {
        var ev = new GameCompletedMetaEvent(100, 42, "u", MiniGameIds.Dice, 10, payout, true, 1, 1);
        var player = new SeasonPlayer(
            SeasonId: 1,
            ChatId: 100,
            UserId: 42,
            DisplayName: "u",
            Xp: 10,
            Level: 1,
            Rating: 1000,
            GamesPlayed: 1,
            Wins: 1,
            Losses: 0,
            TotalStaked: 10,
            TotalPayout: payout,
            UpdatedAt: DateTimeOffset.UtcNow);

        var unlocked = AchievementRegistry.Evaluate(ev, player, 10_000, threshold);

        Assert.Equal(expected, unlocked.Any(x => x.Id == "big_payout"));
    }

    [Fact]
    public void AchievementRegistry_GetAll_BigPayoutDescriptionUsesConfiguredThreshold()
    {
        var achievement = AchievementRegistry.GetAll(1_000, 5_000).Single(x => x.Id == "big_payout");

        Assert.Contains("5 000", achievement.Description);
    }

    [Fact]
    public void RuntimeTuningSanitizer_AllowsMetaSettings()
    {
        var raw = new JsonObject
        {
            ["Games"] = new JsonObject
            {
                ["meta"] = new JsonObject
                {
                    ["HighRollerTotalStaked"] = 1_000,
                    ["BigPayoutMinimum"] = 5_000,
                },
            },
        };

        var sanitized = RuntimeTuningPayloadSanitizer.Sanitize(raw);

        Assert.Equal(1_000, sanitized["Games"]?["meta"]?["HighRollerTotalStaked"]?.GetValue<int>());
        Assert.Equal(5_000, sanitized["Games"]?["meta"]?["BigPayoutMinimum"]?.GetValue<int>());
    }

    [Fact]
    public void QuestRegistry_Matching_LosingDiceGame_MatchesPlayVolumeAndDiceQuestOnly()
    {
        var ev = new GameCompletedMetaEvent(100, 42, "u", MiniGameIds.Dice, 250, 0, false, 0, 1);
        var ids = QuestRegistry.Matching(ev).Select(x => x.Id).ToHashSet(StringComparer.Ordinal);

        Assert.Contains("daily_play_3", ids);
        Assert.Contains("weekly_play_20", ids);
        Assert.Contains("weekly_volume_5000", ids);
        Assert.Contains("daily_dice_1", ids);
        Assert.DoesNotContain("daily_win_1", ids);
        Assert.DoesNotContain("weekly_win_7", ids);
        Assert.DoesNotContain("daily_darts_1", ids);
    }

    [Fact]
    public void QuestRegistry_DeltaFor_VolumeUsesStake_OtherKindsUseOne()
    {
        var ev = new GameCompletedMetaEvent(100, 42, "u", MiniGameIds.Dice, 250, 0, false, 0, 1);
        var volume = QuestRegistry.All.Single(x => x.Id == "weekly_volume_5000");
        var play = QuestRegistry.All.Single(x => x.Id == "daily_play_3");

        Assert.Equal(250, QuestRegistry.DeltaFor(volume, ev));
        Assert.Equal(1, QuestRegistry.DeltaFor(play, ev));
    }

    [Fact]
    public void QuestRegistry_PeriodKey_DailyAndWeeklyAreStable()
    {
        var now = new DateTimeOffset(2026, 5, 20, 12, 0, 0, TimeSpan.Zero);
        var daily = QuestRegistry.All.Single(x => x.Id == "daily_play_3");
        var weekly = QuestRegistry.All.Single(x => x.Id == "weekly_play_20");

        Assert.Equal("2026-05-20", QuestRegistry.PeriodKey(daily, now));
        Assert.Equal("2026-W21", QuestRegistry.PeriodKey(weekly, now));
    }
}

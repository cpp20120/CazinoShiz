using BotFramework.Sdk;
using Games.Meta;
using Xunit;

namespace CasinoShiz.Tests;

public sealed class MetaRegistryTests
{
    [Fact]
    public void AchievementRegistry_AllIdsAreUnique()
    {
        var ids = AchievementRegistry.All.Select(x => x.Id).ToArray();
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
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

using BotFramework.Sdk;
using Games.Meta;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CasinoShiz.Tests;

public sealed class MetaXpProjectionTests
{
    [Fact]
    public async Task HandleAsync_NewPlayDayAndAchievement_PublishesAnalyticsEvents()
    {
        var occurredAt = new DateTimeOffset(2026, 6, 13, 8, 0, 0, TimeSpan.Zero)
            .ToUnixTimeMilliseconds();
        var meta = new RecordingMetaService
        {
            StreakResult = new GameStreakRecordResult(
                new GameStreak(
                    7,
                    100,
                    42,
                    MiniGameIds.Darts,
                    3,
                    3,
                    3,
                    new DateOnly(2026, 6, 13),
                    DateTimeOffset.UtcNow),
                Advanced: true),
            Unlocks =
            [
                new AchievementUnlock(
                    "streak_darts_3",
                    7,
                    100,
                    42,
                    DateTimeOffset.UtcNow),
            ],
        };
        var events = new NullEventBus();
        var projection = new MetaXpProjection(
            meta,
            new NullRiskService(),
            new NullMetaHistoryStore(),
            new FakeRuntimeTuning(),
            events,
            NullLogger<MetaXpProjection>.Instance);

        await ((IDomainEventSubscriber)projection).HandleAsync(
            new GameCompletedMetaEvent(
                100,
                42,
                "player",
                MiniGameIds.Darts,
                10,
                20,
                true,
                2,
                occurredAt),
            CancellationToken.None);

        var streakEvent = Assert.Single(events.Published.OfType<GameStreakUpdatedMetaEvent>());
        Assert.Equal("meta.game_streak_updated", streakEvent.EventType);
        Assert.Equal(3, streakEvent.CurrentStreak);
        Assert.Equal(new DateOnly(2026, 6, 13), streakEvent.PlayedOn);

        var achievementEvent = Assert.Single(events.Published.OfType<AchievementUnlockedMetaEvent>());
        Assert.Equal("meta.achievement_unlocked", achievementEvent.EventType);
        Assert.Equal("streak_darts_3", achievementEvent.AchievementId);
        Assert.Equal("streaks", achievementEvent.Category);
        Assert.Equal(MiniGameIds.Darts, achievementEvent.GameKey);
    }

    [Fact]
    public async Task HandleAsync_RepeatedSameDayWithoutUnlocks_DoesNotPublishAnalyticsEvents()
    {
        var meta = new RecordingMetaService
        {
            StreakResult = new GameStreakRecordResult(
                new GameStreak(
                    7,
                    100,
                    42,
                    MiniGameIds.Darts,
                    3,
                    3,
                    3,
                    new DateOnly(2026, 6, 13),
                    DateTimeOffset.UtcNow),
                Advanced: false),
        };
        var events = new NullEventBus();
        var projection = new MetaXpProjection(
            meta,
            new NullRiskService(),
            new NullMetaHistoryStore(),
            new FakeRuntimeTuning(),
            events,
            NullLogger<MetaXpProjection>.Instance);

        await ((IDomainEventSubscriber)projection).HandleAsync(
            new GameCompletedMetaEvent(
                100,
                42,
                "player",
                MiniGameIds.Darts,
                10,
                0,
                false,
                0,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()),
            CancellationToken.None);

        Assert.Empty(events.Published);
    }

    private sealed class RecordingMetaService : IMetaService
    {
        private readonly MetaSeason _season = new(
            7,
            "test",
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(30),
            "active",
            "{}");

        public GameStreakRecordResult? StreakResult { get; init; }
        public IReadOnlyList<AchievementUnlock> Unlocks { get; init; } = [];

        public Task<MetaSeason> GetActiveSeasonAsync(CancellationToken ct) =>
            Task.FromResult(_season);

        public Task<SeasonPlayer> ApplyGameCompletedAsync(
            long chatId,
            long userId,
            string displayName,
            long stake,
            long payout,
            bool isWin,
            CancellationToken ct) =>
            Task.FromResult(new SeasonPlayer(
                _season.Id,
                chatId,
                userId,
                displayName,
                10,
                1,
                1_000,
                2,
                isWin ? 1 : 0,
                isWin ? 1 : 2,
                stake,
                payout,
                DateTimeOffset.UtcNow));

        public Task<GameStreakRecordResult?> RecordGamePlayedAsync(
            long seasonId,
            long chatId,
            long userId,
            string gameKey,
            DateOnly playedOn,
            CancellationToken ct) =>
            Task.FromResult(StreakResult);

        public Task<IReadOnlyList<AchievementUnlock>> UnlockAchievementsAsync(
            long seasonId,
            long chatId,
            long userId,
            IEnumerable<AchievementDefinition> achievements,
            CancellationToken ct) =>
            Task.FromResult(Unlocks);

        public Task<SeasonProfile> GetProfileAsync(
            long chatId,
            long userId,
            string displayName,
            CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<SeasonLeaderboardEntry>> GetTopAsync(
            long chatId,
            int limit,
            CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<PlayerAchievementView>> GetAchievementsAsync(
            long chatId,
            long userId,
            CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<PlayerGameStreakView>> GetGameStreaksAsync(
            long chatId,
            long userId,
            CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<SeasonPlayer> AddSeasonXpAsync(
            long seasonId,
            long chatId,
            long userId,
            string displayName,
            long xpDelta,
            CancellationToken ct) =>
            throw new NotSupportedException();
    }

    private sealed class NullRiskService : IRiskService
    {
        public Task EvaluateGameCompletedAsync(
            GameCompletedMetaEvent ev,
            SeasonPlayer player,
            CancellationToken ct) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<RiskFlagView>> GetOpenAsync(
            long chatId,
            int limit,
            CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<RiskResolveResult> UpdateStatusAsync(
            long flagId,
            string status,
            CancellationToken ct) =>
            throw new NotSupportedException();
    }

    private sealed class NullMetaHistoryStore : IMetaHistoryStore
    {
        public Task AppendAsync(
            string eventType,
            string aggregateType,
            string aggregateId,
            long? seasonId,
            long? chatId,
            long? userId,
            object payload,
            CancellationToken ct) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<MetaHistoryEvent>> ListAsync(
            string? eventType,
            string? aggregateType,
            string? aggregateId,
            long? chatId,
            long? userId,
            int limit,
            CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<MetaHistoryStats> GetStatsAsync(CancellationToken ct) =>
            throw new NotSupportedException();
    }
}

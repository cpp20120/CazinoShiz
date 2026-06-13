using BotFramework.Host.Services;
using BotFramework.Sdk;

namespace Games.Meta;

public sealed class MetaXpProjection(
    IMetaService meta,
    IRiskService risks,
    IMetaHistoryStore history,
    IRuntimeTuningAccessor tuning,
    IDomainEventBus events,
    ILogger<MetaXpProjection> logger)
    : DomainEventSubscriber<GameCompletedMetaEvent>
{
    protected override async Task HandleAsync(GameCompletedMetaEvent ev, CancellationToken ct)
    {
        var season = await meta.GetActiveSeasonAsync(ct);
        var player = await meta.ApplyGameCompletedAsync(
            ev.ChatId,
            ev.UserId,
            ev.DisplayName,
            ev.Stake,
            ev.Payout,
            ev.IsWin,
            ct);

        await history.AppendAsync(
            "game.completed",
            "player",
            $"{season.Id}:{ev.ChatId}:{ev.UserId}",
            season.Id,
            ev.ChatId,
            ev.UserId,
            new
            {
                ev.GameKey,
                ev.DisplayName,
                ev.Stake,
                ev.Payout,
                ev.IsWin,
                ev.Multiplier,
                ev.OccurredAt,
                player.Xp,
                player.Level,
                player.Rating,
                player.GamesPlayed,
                player.Wins,
                player.Losses,
            },
            ct);

        logger.LogDebug(
            "Applied meta XP for user {UserId} in chat {ChatId}: xp={Xp}, level={Level}, rating={Rating}",
            player.UserId,
            player.ChatId,
            player.Xp,
            player.Level,
            player.Rating);

        await risks.EvaluateGameCompletedAsync(ev, player, ct);

        var options = tuning.GetSection<MetaOptions>(MetaOptions.SectionName);
        var achievements = AchievementRegistry.Evaluate(
            ev,
            player,
            options.HighRollerTotalStaked,
            options.BigPayoutMinimum).ToList();
        GameStreakUpdatedMetaEvent? streakUpdated = null;
        var streakResult = await meta.RecordGamePlayedAsync(
            season.Id,
            ev.ChatId,
            ev.UserId,
            ev.GameKey,
            GameStreakRegistry.PlayDay(ev.OccurredAt, options.StreakTimezoneOffsetHours),
            ct);
        if (streakResult is { } result)
        {
            achievements.AddRange(GameStreakRegistry.Evaluate(result.Streak));
            if (result.Advanced)
            {
                streakUpdated = new GameStreakUpdatedMetaEvent(
                    result.Streak.SeasonId,
                    result.Streak.ChatId,
                    result.Streak.UserId,
                    result.Streak.GameKey,
                    result.Streak.CurrentStreak,
                    result.Streak.BestStreak,
                    result.Streak.TotalPlayDays,
                    result.Streak.LastPlayedOn,
                    ev.OccurredAt);
            }
        }

        var unlocked = await meta.UnlockAchievementsAsync(
            season.Id,
            ev.ChatId,
            ev.UserId,
            achievements,
            ct);

        if (streakUpdated is not null)
            await PublishAnalyticsAsync(streakUpdated, ct);

        var definitions = achievements
            .GroupBy(x => x.Id, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);
        foreach (var achievement in unlocked)
        {
            await history.AppendAsync(
                "achievement.unlocked",
                "player",
                $"{achievement.SeasonId}:{achievement.ChatId}:{achievement.UserId}",
                achievement.SeasonId,
                achievement.ChatId,
                achievement.UserId,
                new { achievement.AchievementId, achievement.UnlockedAt },
                ct);

            if (definitions.TryGetValue(achievement.AchievementId, out var definition))
            {
                await PublishAnalyticsAsync(
                    new AchievementUnlockedMetaEvent(
                        achievement.SeasonId,
                        achievement.ChatId,
                        achievement.UserId,
                        achievement.AchievementId,
                        definition.Title,
                        definition.Category,
                        ev.GameKey,
                        ev.OccurredAt),
                    ct);
            }

            logger.LogInformation(
                "Unlocked achievement {AchievementId} for user {UserId} in chat {ChatId}, season {SeasonId}",
                achievement.AchievementId,
                achievement.UserId,
                achievement.ChatId,
                achievement.SeasonId);
        }
    }

    private async Task PublishAnalyticsAsync(IDomainEvent ev, CancellationToken ct)
    {
        try
        {
            await events.PublishAsync(ev, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to publish meta analytics event {EventType}", ev.EventType);
        }
    }
}

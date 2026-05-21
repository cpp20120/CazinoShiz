using BotFramework.Sdk;

namespace Games.Meta;

public sealed class MetaXpProjection(
    IMetaService meta,
    IRiskService risks,
    IMetaHistoryStore history,
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

        var achievements = AchievementRegistry.Evaluate(ev, player);
        var unlocked = await meta.UnlockAchievementsAsync(
            season.Id,
            ev.ChatId,
            ev.UserId,
            achievements,
            ct);

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

            logger.LogInformation(
                "Unlocked achievement {AchievementId} for user {UserId} in chat {ChatId}, season {SeasonId}",
                achievement.AchievementId,
                achievement.UserId,
                achievement.ChatId,
                achievement.SeasonId);
        }
    }
}

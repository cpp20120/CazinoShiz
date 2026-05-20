using BotFramework.Sdk;

namespace Games.Meta;

public sealed class MetaXpProjection(IMetaService meta, ILogger<MetaXpProjection> logger)
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

        logger.LogDebug(
            "Applied meta XP for user {UserId} in chat {ChatId}: xp={Xp}, level={Level}, rating={Rating}",
            player.UserId,
            player.ChatId,
            player.Xp,
            player.Level,
            player.Rating);

        var achievements = AchievementRegistry.Evaluate(ev, player);
        var unlocked = await meta.UnlockAchievementsAsync(
            season.Id,
            ev.ChatId,
            ev.UserId,
            achievements,
            ct);

        foreach (var achievement in unlocked)
        {
            logger.LogInformation(
                "Unlocked achievement {AchievementId} for user {UserId} in chat {ChatId}, season {SeasonId}",
                achievement.AchievementId,
                achievement.UserId,
                achievement.ChatId,
                achievement.SeasonId);
        }
    }
}

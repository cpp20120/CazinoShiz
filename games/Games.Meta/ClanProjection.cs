using BotFramework.Sdk;

namespace Games.Meta;

public sealed class ClanProjection(IClanService clans, ILogger<ClanProjection> logger)
    : DomainEventSubscriber<GameCompletedMetaEvent>
{
    protected override async Task HandleAsync(GameCompletedMetaEvent ev, CancellationToken ct)
    {
        await clans.ApplyGameCompletedAsync(ev, ct);
        logger.LogDebug(
            "Applied clan XP projection for user {UserId} in chat {ChatId}, game {GameKey}",
            ev.UserId,
            ev.ChatId,
            ev.GameKey);
    }
}

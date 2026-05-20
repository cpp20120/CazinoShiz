using BotFramework.Sdk;

namespace Games.Meta;

public sealed class MetaXpProjection(IMetaService meta, ILogger<MetaXpProjection> logger)
    : DomainEventSubscriber<GameCompletedMetaEvent>
{
    protected override async Task HandleAsync(GameCompletedMetaEvent ev, CancellationToken ct)
    {
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
    }
}

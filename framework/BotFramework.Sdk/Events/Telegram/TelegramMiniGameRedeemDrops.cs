namespace BotFramework.Sdk;
public static class TelegramMiniGameRedeemDrops
{
    public static Task MaybePublishAsync(
        IDomainEventBus events,
        double chance,
        long userId,
        long chatId,
        string gameId,
        long occurredAt,
        CancellationToken ct)
    {
        if (!ShouldDrop(chance))
            return Task.CompletedTask;

        return events.PublishAsync(
            new TelegramMiniGameRedeemCodeDropRequested(userId, chatId, gameId, occurredAt),
            ct);
    }

    private static bool ShouldDrop(double chance)
    {
        if (chance <= 0) return false;
        if (chance >= 1) return true;
        return Random.Shared.NextDouble() < chance;
    }
}

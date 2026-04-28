namespace BotFramework.Sdk;

public sealed record TelegramMiniGameRedeemCodeDropRequested(
    long UserId,
    long ChatId,
    string GameId,
    long OccurredAt) : IDomainEvent
{
    public string EventType => "telegram_dice.redeem_code_drop_requested";
}

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

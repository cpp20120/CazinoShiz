namespace BotFramework.Sdk;
public sealed record TelegramMiniGameRedeemCodeDropRequested(
    long UserId,
    long ChatId,
    string GameId,
    long OccurredAt) : IDomainEvent
{
    public string EventType => "telegram_dice.redeem_code_drop_requested";
}

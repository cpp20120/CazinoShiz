namespace BotFramework.Sdk;
/// <summary>
/// Emitted only when an achievement is newly inserted for the player.
/// </summary>
public sealed record AchievementUnlockedMetaEvent(
    long SeasonId,
    long ChatId,
    long UserId,
    string AchievementId,
    string Title,
    string Category,
    string GameKey,
    long OccurredAt) : IDomainEvent
{
    public string EventType => "meta.achievement_unlocked";
}

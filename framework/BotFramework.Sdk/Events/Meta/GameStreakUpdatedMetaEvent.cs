namespace BotFramework.Sdk;
/// <summary>
/// Emitted once per player, game, and local play day after streak progress changes.
/// Mirrored to analytics for retention and per-game streak reporting.
/// </summary>
public sealed record GameStreakUpdatedMetaEvent(
    long SeasonId,
    long ChatId,
    long UserId,
    string GameKey,
    int CurrentStreak,
    int BestStreak,
    int TotalPlayDays,
    DateOnly PlayedOn,
    long OccurredAt) : IDomainEvent
{
    public string EventType => "meta.game_streak_updated";
}

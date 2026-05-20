namespace Games.Meta;

public sealed record AchievementDefinition(
    string Id,
    string Title,
    string Description,
    string Category,
    bool IsSeasonal,
    bool IsSecret);

public sealed record PlayerAchievementView(
    string Id,
    string Title,
    string Description,
    string Category,
    bool IsUnlocked,
    DateTimeOffset? UnlockedAt);

public sealed record AchievementUnlock(
    string AchievementId,
    long SeasonId,
    long ChatId,
    long UserId,
    DateTimeOffset UnlockedAt);

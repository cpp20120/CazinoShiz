namespace Games.Meta;

public sealed record GameStreak(
    long SeasonId,
    long ChatId,
    long UserId,
    string GameKey,
    int CurrentStreak,
    int BestStreak,
    int TotalPlayDays,
    DateOnly LastPlayedOn,
    DateTimeOffset UpdatedAt);

public sealed record GameStreakRecordResult(GameStreak Streak, bool Advanced);

public sealed record PlayerGameStreakView(
    string GameKey,
    string Title,
    string Command,
    int CurrentStreak,
    int BestStreak,
    int TotalPlayDays,
    DateOnly? LastPlayedOn);

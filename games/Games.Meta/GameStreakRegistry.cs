using BotFramework.Sdk;

namespace Games.Meta;

public static class GameStreakRegistry
{
    public static IReadOnlyList<GameStreakDefinition> Games { get; } =
    [
        new(MiniGameIds.Dice, "Слот-машина", "/slots"),
        new(MiniGameIds.DiceCube, "Куб", "/dice"),
        new(MiniGameIds.Darts, "Дартс", "/darts"),
        new(MiniGameIds.Football, "Футбол", "/football"),
        new(MiniGameIds.Basketball, "Баскетбол", "/basketball"),
        new(MiniGameIds.Bowling, "Боулинг", "/bowling"),
    ];

    public static IReadOnlyList<int> AchievementDays { get; } = [3, 7, 14];

    public static bool Supports(string gameKey) =>
        Games.Any(x => string.Equals(x.GameKey, gameKey, StringComparison.Ordinal));

    public static IReadOnlyList<AchievementDefinition> GetAchievements() =>
        Games.SelectMany(game => AchievementDays.Select(days => CreateAchievement(game, days)))
            .ToList();

    public static IReadOnlyList<AchievementDefinition> Evaluate(GameStreak streak)
    {
        if (!Supports(streak.GameKey)) return [];

        var game = Games.Single(x => x.GameKey == streak.GameKey);
        return AchievementDays
            .Where(days => streak.CurrentStreak >= days)
            .Select(days => CreateAchievement(game, days))
            .ToList();
    }

    public static DateOnly PlayDay(long occurredAt, int timezoneOffsetHours)
    {
        var occurred = DateTimeOffset.FromUnixTimeMilliseconds(occurredAt);
        var local = occurred.ToOffset(TimeSpan.FromHours(Math.Clamp(timezoneOffsetHours, -14, 14)));
        return DateOnly.FromDateTime(local.DateTime);
    }

    public static int ActiveStreak(int currentStreak, DateOnly lastPlayedOn, DateOnly today) =>
        lastPlayedOn < today.AddDays(-1) ? 0 : currentStreak;

    private static AchievementDefinition CreateAchievement(GameStreakDefinition game, int days) =>
        new(
            AchievementId(game.GameKey, days),
            $"{game.Title}: {days} дней",
            $"Играть в {game.Command} {days} дней подряд.",
            "streaks",
            true,
            false);

    private static string AchievementId(string gameKey, int days) =>
        $"streak_{gameKey}_{days}";
}

public sealed record GameStreakDefinition(string GameKey, string Title, string Command);

using BotFramework.Sdk;

namespace Games.Meta;

public static class QuestRegistry
{
    public static IReadOnlyList<QuestTemplate> All { get; } =
    [
        new("daily_play_3", "Разогрев", "Сыграй 3 любые игры сегодня.", "daily", "play", null, 3, 75, 100),
        new("daily_win_1", "Первая кровь", "Выиграй 1 игру сегодня.", "daily", "win", null, 1, 100, 150),
        new("daily_dice_1", "Слотовый импульс", "Сыграй в /dice сегодня.", "daily", "play_game", MiniGameIds.Dice, 1, 50, 75),
        new("daily_darts_1", "Бросок дня", "Сыграй в /darts сегодня.", "daily", "play_game", MiniGameIds.Darts, 1, 50, 75),
        new("weekly_play_20", "Недельный гринд", "Сыграй 20 любых игр за неделю.", "weekly", "play", null, 20, 500, 1000),
        new("weekly_win_7", "Победитель недели", "Выиграй 7 игр за неделю.", "weekly", "win", null, 7, 750, 1250),
        new("weekly_volume_5000", "Оборотистый", "Поставь суммарно 5000 монет за неделю.", "weekly", "volume", null, 5000, 650, 1000),
    ];

    public static IEnumerable<QuestTemplate> Matching(GameCompletedMetaEvent ev)
    {
        foreach (var quest in All)
        {
            if (quest.Kind == "play")
            {
                yield return quest;
            }
            else if (quest.Kind == "win" && ev.IsWin)
            {
                yield return quest;
            }
            else if (quest.Kind == "volume" && ev.Stake > 0)
            {
                yield return quest;
            }
            else if (quest.Kind == "play_game" && quest.GameKey == ev.GameKey)
            {
                yield return quest;
            }
        }
    }

    public static int DeltaFor(QuestTemplate quest, GameCompletedMetaEvent ev) =>
        quest.Kind == "volume" ? (int)Math.Min(int.MaxValue, Math.Max(0, ev.Stake)) : 1;

    public static string PeriodKey(QuestTemplate quest, DateTimeOffset now) => quest.Period switch
    {
        "weekly" => $"{now:yyyy}-W{System.Globalization.ISOWeek.GetWeekOfYear(now.DateTime):00}",
        _ => now.ToString("yyyy-MM-dd"),
    };
}

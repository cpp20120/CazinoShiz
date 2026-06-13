using BotFramework.Sdk;

namespace Games.Meta;

public static class AchievementRegistry
{
    public static IReadOnlyList<AchievementDefinition> All => GetAll(1_000, 1_000);

    public static IReadOnlyList<AchievementDefinition> GetAll(
        long highRollerTotalStaked,
        long bigPayoutMinimum = 1_000) =>
    [
        new("first_game", "Первый заход", "Сыграть первую игру в сезоне.", "season", true, false),
        new("first_win", "Первая победа", "Выиграть первую игру в сезоне.", "season", true, false),
        new("ten_games", "Разогрев", "Сыграть 10 игр в сезоне.", "season", true, false),
        new("fifty_games", "Гриндер", "Сыграть 50 игр в сезоне.", "season", true, false),
        new("ten_wins", "Победная серия", "Выиграть 10 игр в сезоне.", "season", true, false),
        new(
            "high_roller",
            "High Roller",
            $"Поставить суммарно {FormatCoins(highRollerTotalStaked)} монет за сезон.",
            "economy",
            true,
            false),
        new(
            "big_payout",
            "Большой занос",
            $"Получить выплату от {FormatCoins(bigPayoutMinimum)} монет за одну игру.",
            "economy",
            true,
            false),
        new("dice_player", "Слоты крутятся", "Сыграть в слот-машину /slots.", "games", true, false),
        new("cube_player", "Кубовод", "Сыграть в /dice.", "games", true, false),
        new("darts_player", "В яблочко", "Сыграть в /darts.", "games", true, false),
        new("football_player", "Гол-машина", "Сыграть в /football.", "games", true, false),
        new("basketball_player", "Бросок сезона", "Сыграть в /basketball.", "games", true, false),
        new("bowling_player", "Страйк рядом", "Сыграть в /bowling.", "games", true, false),
        .. GameStreakRegistry.GetAchievements(),
    ];

    public static IReadOnlyList<AchievementDefinition> Evaluate(
        GameCompletedMetaEvent ev,
        SeasonPlayer player,
        long highRollerTotalStaked = 1_000,
        long bigPayoutMinimum = 1_000)
    {
        var definitions = GetAll(highRollerTotalStaked, bigPayoutMinimum);
        var unlocked = new List<AchievementDefinition>();

        AddIf(definitions, unlocked, "first_game", player.GamesPlayed >= 1);
        AddIf(definitions, unlocked, "first_win", player.Wins >= 1);
        AddIf(definitions, unlocked, "ten_games", player.GamesPlayed >= 10);
        AddIf(definitions, unlocked, "fifty_games", player.GamesPlayed >= 50);
        AddIf(definitions, unlocked, "ten_wins", player.Wins >= 10);
        AddIf(definitions, unlocked, "high_roller", player.TotalStaked >= Math.Max(1, highRollerTotalStaked));
        AddIf(definitions, unlocked, "big_payout", ev.Payout >= Math.Max(1, bigPayoutMinimum));

        AddIf(definitions, unlocked, "dice_player", ev.GameKey == MiniGameIds.Dice);
        AddIf(definitions, unlocked, "cube_player", ev.GameKey == MiniGameIds.DiceCube);
        AddIf(definitions, unlocked, "darts_player", ev.GameKey == MiniGameIds.Darts);
        AddIf(definitions, unlocked, "football_player", ev.GameKey == MiniGameIds.Football);
        AddIf(definitions, unlocked, "basketball_player", ev.GameKey == MiniGameIds.Basketball);
        AddIf(definitions, unlocked, "bowling_player", ev.GameKey == MiniGameIds.Bowling);

        return unlocked;
    }

    private static void AddIf(
        IReadOnlyList<AchievementDefinition> definitions,
        List<AchievementDefinition> result,
        string id,
        bool condition)
    {
        if (!condition) return;
        var achievement = definitions.First(x => x.Id == id);
        result.Add(achievement);
    }

    private static string FormatCoins(long value) =>
        Math.Max(1, value).ToString("N0", System.Globalization.CultureInfo.InvariantCulture).Replace(',', ' ');
}

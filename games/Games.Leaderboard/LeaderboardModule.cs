using BotFramework.Sdk;

namespace Games.Leaderboard;

public sealed class LeaderboardModule : IModule
{
    public string Id => "leaderboard";
    public string DisplayName => "🏆 Топ";
    public string Version => "1.0.0";

    public void ConfigureServices(IModuleServiceCollection services)
    {
        services
            .BindOptions<LeaderboardOptions>(LeaderboardOptions.SectionName)
            .AddScoped<ILeaderboardStore, LeaderboardStore>()
            .AddScoped<ILeaderboardService, LeaderboardService>()
            .AddHandler<LeaderboardHandler>();
    }

    public IReadOnlyList<BotCommand> GetBotCommands() =>
    [
        new BotCommand("/top", "leaderboard.cmd.top"),
        new BotCommand("/balance", "leaderboard.cmd.balance"),
        new BotCommand("/help", "leaderboard.cmd.help"),
    ];

    public IReadOnlyList<LocaleBundle> GetLocales() =>
    [
        new LocaleBundle("ru", new Dictionary<string, string>
        {
            ["display_name"] = "Топ",
            ["cmd.top"] = "Топ игроков",
            ["cmd.balance"] = "Мой баланс",
            ["cmd.help"] = "Помощь",

            ["top.header"] = "🏆 <b>Топ игроков</b>",
            ["top.empty"] = "Опа, в топе никого! Похоже никто не крутил последнее время!",
            ["top.truncated"] = "…\n(используй /top full чтобы показать всех)",
            ["top.hidden_reminder"] = "\n<i>Игроки, не заходившие несколько дней, скрыты из топа.</i>",

            ["balance.visible"] = "💰 Твой баланс: <b>{0}</b>",
            ["balance.hidden"] = "💰 Твой баланс: <b>{0}</b>\n<i>Ты скрыт из топа из-за неактивности.</i>",

            ["help"] = "🎰 <b>CasinoShiz</b>\n\n"
                + "/top — таблица лидеров\n"
                + "/balance — твой баланс\n"
                + "/redeem <i>код</i> — активировать код\n\n"
                + "Игры: /sh · /poker · /blackjack · /horse · /dicecube · /darts · /football · /basketball · /bowling\n"
                + "Слоты: отправь 🎰 в чат\n\n"
                + "🐎 Скачки — правила: /horse help"
        }),
    ];
}

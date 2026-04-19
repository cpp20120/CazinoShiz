using BotFramework.Sdk;

namespace Games.Horse;

public sealed class HorseModule : IModule
{
    public string Id => "horse";
    public string DisplayName => "🐎 Скачки";
    public string Version => "1.0.0";

    public void ConfigureServices(IModuleServiceCollection services)
    {
        services
            .BindOptions<HorseOptions>(HorseOptions.SectionName)
            .AddScoped<IHorseService, HorseService>()
            .AddScoped<IHorseBetStore, HorseBetStore>()
            .AddScoped<IHorseResultStore, HorseResultStore>()
            .AddHandler<HorseHandler>();
    }

    public IModuleMigrations GetMigrations() => new HorseMigrations();

    public IReadOnlyList<BotCommand> GetBotCommands() =>
    [
        new BotCommand("/horse", "horse.cmd.horse"),
    ];

    public IReadOnlyList<LocaleBundle> GetLocales() =>
    [
        new LocaleBundle("ru", new Dictionary<string, string>
        {
            ["display_name"] = "Скачки",
            ["cmd.horse"] = "Поставить на скачки",
            ["help"] =
                "🐎 <b>Скачки</b>\n\n" +
                "/horse bet <i>номер</i> <i>ставка</i> — поставить на лошадь (1–{0}).\n" +
                "/horse info — коэффициенты и число ставок на сегодня.\n" +
                "/horse result — результат последнего забега.\n" +
                "/horse help — эта справка.",
            ["unknown_action"] = "Неизвестное действие {0}\nМожно: bet, result, info",
            ["bet.no_horse"] = "Вы не указали номер лошади (1-{0})",
            ["bet.no_amount"] = "Вы не указали ставку",
            ["bet.accepted"] = "Вы поставили {0} на лошадь под номером {1} на следующие скачки!",
            ["bet.invalid_horse"] = "Указан неверный номер лошади (1-{0})",
            ["bet.invalid_amount"] = "Указана неверная ставка (ваш баланс: {0})",
            ["bet.failed"] = "Не удалось поставить ставку",
            ["run.not_enough_bets"] = "Недостаточно ставок для забега (нужно {0})",
            ["run.started"] = "Активность запущена",
            ["run.winners_header"] = "<b>Поздравляем победителей!</b>",
            ["run.winner_line"] = "<a href=\"tg://user?id={1}\">Победитель {0}</a>: <b>+{2}</b>",
            ["run.no_winners"] = "<b>Сегодня никому не удалось победить :(</b>",
            ["result.winner"] = "Выиграла лошадь {0}",
            ["result.none"] = "Сегодня скачки еще не проводились",
            ["info.stakes_count"] = "Ставок на сегодня: {0}",
            ["info.koefs_header"] = "<b>Коэффициенты:</b>",
            ["info.koef_line"] = "🐎 {0}: <b>{1}</b>",
        }),
    ];
}

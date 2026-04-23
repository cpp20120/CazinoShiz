using BotFramework.Sdk;

namespace Games.Football;

public sealed class FootballModule : IModule
{
    public string Id => "football";
    public string DisplayName => "⚽ Футбол";
    public string Version => "1.0.0";

    public void ConfigureServices(IModuleServiceCollection services)
    {
        services
            .BindOptions<FootballOptions>(FootballOptions.SectionName)
            .AddScoped<IFootballService, FootballService>()
            .AddScoped<IFootballBetStore, FootballBetStore>()
            .AddHandler<FootballHandler>();
    }

    public IModuleMigrations GetMigrations() => new FootballMigrations();

    public IReadOnlyList<BotCommand> GetBotCommands() =>
    [
        new BotCommand("/football", "football.cmd.football"),
    ];

    public IReadOnlyList<LocaleBundle> GetLocales() =>
    [
        new LocaleBundle("ru", new Dictionary<string, string>
        {
            ["display_name"] = "Футбол",
            ["cmd.football"] = "Поставить на футбол (⚽)",
            ["usage"] = "⚽ <b>Футбол</b>\n"
                + "<code>/football</code> или <code>/football bet</code> — ставка по умолчанию <b>{0}</b> (как при явной сумме).\n"
                + "<code>/football bet &lt;сумма&gt;</code> — своя ставка. Справка: <code>/football help</code>",
            ["bet.usage"] = "Неверная сумма. Примеры: <code>/football</code>, <code>/football bet</code> (={0}) или <code>/football bet 50</code>",
            ["bet.accepted"] = "Ставка {0} принята. Сейчас бот кинет ⚽ — свой ⚽ не отправляй.\nВыплаты: 4→x2, 5→x2",
            ["roll.wait_bot"] = "Эта ставка ждёт бросок <b>от бота</b>. Свой ⚽ не отправляй — один бросок на ставку.",
            ["bet.invalid"] = "Неверная сумма ставки",
            ["bet.not_enough"] = "Недостаточно монет (баланс: {0})",
            ["bet.already_pending"] = "У тебя уже есть ставка {0} в этом чате — дождись броска бота ⚽",
            ["bet.busy_other"] = "Сначала дождись броска бота в {0} — в этом чате только одна активная мини-игра.",
            ["bet.failed"] = "Не удалось принять ставку",
            ["throw.no_bet"] = "Сначала сделай ставку: <code>/football</code>, <code>/football bet</code> или <code>/football bet &lt;сумма&gt;</code>.",
            ["throw.win"] = "Выпало <b>{0}</b> — x{1}. Ставка: {2} · выплата: <b>{3}</b> · чистыми: <b>+{4}</b>. Баланс: {5}",
            ["throw.lose"] = "Выпало <b>{0}</b>. <b>Проигрыш: −{1}</b> монет (ставка сгорела). Баланс: {2}",
        }),
    ];
}

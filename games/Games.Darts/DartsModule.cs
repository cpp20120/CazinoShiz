using BotFramework.Sdk;

namespace Games.Darts;

public sealed class DartsModule : IModule
{
    public string Id => "darts";
    public string DisplayName => "🎯 Дартс";
    public string Version => "1.0.0";

    public void ConfigureServices(IModuleServiceCollection services)
    {
        services
            .BindOptions<DartsOptions>(DartsOptions.SectionName)
            .AddSingleton<IDartsRollQueue, DartsRollQueue>()
            .AddSingleton<DartsBotDiceSender>()
            .AddScoped<IDartsRoundStore, DartsRoundStore>()
            .AddScoped<IDartsService, DartsService>()
            .AddBackgroundJob<DartsRollDispatcherJob>()
            .AddHandler<DartsHandler>();
    }

    public IModuleMigrations GetMigrations() => new DartsMigrations();

    public IReadOnlyList<BotCommand> GetBotCommands() =>
    [
        new BotCommand("/darts", "darts.cmd.darts"),
    ];

    public IReadOnlyList<LocaleBundle> GetLocales() =>
    [
        new LocaleBundle("ru", new Dictionary<string, string>
        {
            ["display_name"] = "Дартс",
            ["cmd.darts"] = "Поставить на дартс",
            ["usage"] = "🎯 <b>Дартс</b>\n"
                + "<code>/darts</code> или <code>/darts bet</code> — ставка по умолчанию <b>{0}</b> (как при явной сумме).\n"
                + "<code>/darts bet &lt;сумма&gt;</code> — своя ставка. Справка: <code>/darts help</code>",
            ["bet.usage"] = "Неверная сумма. Примеры: <code>/darts</code>, <code>/darts bet</code> (={0}) или <code>/darts bet 50</code>",
            ["bet.accepted"] = "Ставка {0} принята. Бот кинет 🎯 по очереди в этом чате — свой 🎯 не отправляй.\nВыплаты: 4→x1, 5→x2, 6 (в яблочко)→x2",
            ["bet.queue_ahead"] = "Перед твоим броском в этом чате ещё <b>{0}</b> — подожди очередь.",
            ["roll.wait_bot"] = "Эта ставка ждёт бросок <b>от бота</b>. Свой 🎯 не отправляй — один бросок на ставку.",
            ["bet.invalid"] = "Неверная сумма ставки",
            ["bet.not_enough"] = "Недостаточно монет (баланс: {0})",
            ["bet.busy_other"] = "Сначала дождись броска бота в {0} — в этом чате только одна активная мини-игра.",
            ["bet.daily_roll_limit"] = "Лимит бросков дартса на сегодня исчерпан ({0}/{1}). Попробуй завтра. 🎯",
            ["bet.failed"] = "Не удалось принять ставку",
            ["throw.no_bet"] = "Сначала сделай ставку: <code>/darts</code>, <code>/darts bet</code> или <code>/darts bet &lt;сумма&gt;</code>.",
            ["throw.quick_wait"] = "⏳ Нет ставки — применяю ставку по умолчанию и считаю результат…",
            ["throw.win"] = "Выпало <b>{0}</b> — x{1}. Ставка: {2} · выплата: <b>{3}</b> · чистыми: <b>+{4}</b>. Баланс: {5}",
            ["throw.lose"] = "Выпало <b>{0}</b>. <b>Проигрыш: −{1}</b> монет (ставка сгорела). Баланс: {2}",
        }),
    ];
}

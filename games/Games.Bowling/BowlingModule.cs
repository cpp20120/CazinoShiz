using BotFramework.Sdk;

namespace Games.Bowling;

public sealed class BowlingModule : IModule
{
    public string Id => "bowling";
    public string DisplayName => "🎳 Боулинг";
    public string Version => "1.0.0";

    public void ConfigureServices(IModuleServiceCollection services)
    {
        services
            .BindOptions<BowlingOptions>(BowlingOptions.SectionName)
            .AddScoped<IBowlingService, BowlingService>()
            .AddScoped<IBowlingBetStore, BowlingBetStore>()
            .AddHandler<BowlingHandler>();
    }

    public IModuleMigrations GetMigrations() => new BowlingMigrations();

    public IReadOnlyList<BotCommand> GetBotCommands() =>
    [
        new BotCommand("/bowling", "bowling.cmd.bowling"),
    ];

    public IReadOnlyList<LocaleBundle> GetLocales() =>
    [
        new LocaleBundle("ru", new Dictionary<string, string>
        {
            ["display_name"] = "Боулинг",
            ["cmd.bowling"] = "Поставить на боулинг",
            ["usage"] = "🎳 <b>Боулинг</b>\n"
                + "<code>/bowling</code> или <code>/bowling bet</code> — ставка по умолчанию <b>{0}</b> (то же, что сумма {0}: правила и лимиты те же).\n"
                + "<code>/bowling bet &lt;сумма&gt;</code> — своя ставка.\n"
                + "Справка: <code>/bowling help</code>",
            ["bet.usage"] = "Неверная сумма. Примеры: <code>/bowling</code>, <code>/bowling bet</code> (={0}) или <code>/bowling bet 50</code>",
            ["bet.accepted"] = "Ставка {0} принята. Сейчас бот кинет 🎳 — свой 🎳 не отправляй.\nВыплаты: 4→x1, 5→x2, 6 (страйк)→x2",
            ["roll.wait_bot"] = "Эта ставка ждёт бросок <b>от бота</b>. Свой 🎳 не отправляй — один бросок на ставку.",
            ["bet.invalid"] = "Неверная сумма ставки",
            ["bet.not_enough"] = "Недостаточно монет (баланс: {0})",
            ["bet.already_pending"] = "У тебя уже есть ставка {0} в этом чате — дождись броска бота 🎳",
            ["bet.busy_other"] = "Сначала дождись броска бота в {0} — в этом чате только одна активная мини-игра.",
            ["bet.daily_roll_limit"] = "Лимит бросков боулинга на сегодня исчерпан ({0}/{1}). Попробуй завтра. 🎳",
            ["bet.failed"] = "Не удалось принять ставку",
            ["roll.no_bet"] = "Сначала сделай ставку: <code>/bowling</code>, <code>/bowling bet</code> или <code>/bowling bet &lt;сумма&gt;</code> (перед каждым раундом).",
            ["roll.quick_wait"] = "⏳ Нет ставки — применяю ставку по умолчанию и считаю результат…",
            ["roll.win"] = "Выпало <b>{0}</b> — x{1}. Ставка: {2} · выплата: <b>{3}</b> · чистыми: <b>+{4}</b>. Баланс: {5}",
            ["roll.lose"] = "Выпало <b>{0}</b>. <b>Проигрыш: −{1}</b> монет (ставка сгорела). Баланс: {2}",
        }),
    ];
}

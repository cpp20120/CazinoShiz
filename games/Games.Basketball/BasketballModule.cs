using BotFramework.Sdk;

namespace Games.Basketball;

public sealed class BasketballModule : IModule
{
    public string Id => "basketball";
    public string DisplayName => "🏀 Баскетбол";
    public string Version => "1.0.0";

    public void ConfigureServices(IModuleServiceCollection services)
    {
        services
            .BindOptions<BasketballOptions>(BasketballOptions.SectionName)
            .AddScoped<IBasketballService, BasketballService>()
            .AddScoped<IBasketballBetStore, BasketballBetStore>()
            .AddHandler<BasketballHandler>();
    }

    public IModuleMigrations GetMigrations() => new BasketballMigrations();

    public IReadOnlyList<BotCommand> GetBotCommands() =>
    [
        new BotCommand("/basket", "basketball.cmd.basket"),
    ];

    public IReadOnlyList<LocaleBundle> GetLocales() =>
    [
        new LocaleBundle("ru", new Dictionary<string, string>
        {
            ["display_name"] = "Баскетбол",
            ["cmd.basket"] = "Поставить на баскетбол",
            ["usage"] = "🏀 <b>Баскетбол</b>\n"
                + "<code>/basket</code> или <code>/basket bet</code> — ставка по умолчанию <b>{0}</b> (то же, что явная сумма {0}: правила и лимиты одинаковые).\n"
                + "<code>/basket bet &lt;сумма&gt;</code> — своя ставка.\n"
                + "Справка: <code>/basket help</code>",
            ["bet.usage"] = "Неверная сумма. Примеры: <code>/basket</code>, <code>/basket bet</code> (={0}) или <code>/basket bet 50</code>",
            ["bet.accepted"] = "Ставка {0} принята. Сейчас бот кинет 🏀 — свой 🏀 не отправляй.\nВыплаты: 4 (в кольцо)→x2, 5 (чистый бросок)→x2",
            ["roll.wait_bot"] = "Эта ставка ждёт бросок <b>от бота</b>. Свой 🏀 не отправляй — один бросок на ставку.",
            ["bet.invalid"] = "Неверная сумма ставки",
            ["bet.not_enough"] = "Недостаточно монет (баланс: {0})",
            ["bet.already_pending"] = "У тебя уже есть ставка {0} в этом чате — дождись броска бота 🏀",
            ["bet.busy_other"] = "Сначала дождись броска бота в {0} — в этом чате только одна активная мини-игра.",
            ["bet.daily_roll_limit"] = "Лимит бросков кубика на сегодня исчерпан ({0}/{1}). Попробуй завтра. 🎲",
            ["bet.failed"] = "Не удалось принять ставку",
            ["throw.no_bet"] = "Сначала сделай ставку: <code>/basket</code>, <code>/basket bet</code> или <code>/basket bet &lt;сумма&gt;</code> (можно так перед каждым раундом).",
            ["throw.quick_wait"] = "⏳ Нет ставки — применяю ставку по умолчанию и считаю результат…",
            ["throw.win"] = "Выпало <b>{0}</b> — x{1}. Ставка: {2} · выплата: <b>{3}</b> · чистыми: <b>+{4}</b>. Баланс: {5}",
            ["throw.lose"] = "Выпало <b>{0}</b>. <b>Проигрыш: −{1}</b> монет (ставка сгорела). Баланс: {2}",
        }),
    ];
}

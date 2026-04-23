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
            ["usage"] = "Используй: <code>/basket bet &lt;сумма&gt;</code>, затем брось мяч 🏀",
            ["bet.usage"] = "Укажи ставку: <code>/basket bet 50</code>",
            ["bet.accepted"] = "Ставка {0} принята. Теперь брось мяч 🏀\nВыплаты: 4 (в кольцо)→x2, 5 (чистый бросок)→x2",
            ["bet.invalid"] = "Неверная сумма ставки",
            ["bet.not_enough"] = "Недостаточно монет (баланс: {0})",
            ["bet.already_pending"] = "У тебя уже есть ставка {0} в этом чате — брось мяч 🏀",
            ["bet.failed"] = "Не удалось принять ставку",
            ["throw.no_bet"] = "Сначала сделай ставку: <code>/basket bet &lt;сумма&gt;</code>",
            ["throw.win"] = "Выпало <b>{0}</b> — x{1}! Ты забираешь <b>{2}</b> монет. Баланс: {3}",
            ["throw.lose"] = "Выпало <b>{0}</b> — увы, твоя ставка {1} сгорела. Баланс: {2}",
        }),
    ];
}

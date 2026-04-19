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
            ["usage"] = "Используй: <code>/bowling bet &lt;сумма&gt;</code>, затем кинь шар 🎳",
            ["bet.usage"] = "Укажи ставку: <code>/bowling bet 50</code>",
            ["bet.accepted"] = "Ставка {0} принята. Теперь кинь шар 🎳\nВыплаты: 4→x2, 5→x3, 6 (страйк)→x6",
            ["bet.invalid"] = "Неверная сумма ставки",
            ["bet.not_enough"] = "Недостаточно монет (баланс: {0})",
            ["bet.already_pending"] = "У тебя уже есть ставка {0} в этом чате — кинь шар 🎳",
            ["bet.failed"] = "Не удалось принять ставку",
            ["roll.no_bet"] = "Сначала сделай ставку: <code>/bowling bet &lt;сумма&gt;</code>",
            ["roll.win"] = "Выпало <b>{0}</b> — x{1}! Ты забираешь <b>{2}</b> монет. Баланс: {3}",
            ["roll.lose"] = "Выпало <b>{0}</b> — увы, твоя ставка {1} сгорела. Баланс: {2}",
        }),
    ];
}

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
            .AddScoped<IDartsService, DartsService>()
            .AddScoped<IDartsBetStore, DartsBetStore>()
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
            ["usage"] = "Используй: <code>/darts bet &lt;сумма&gt;</code>, затем кинь дротик 🎯",
            ["bet.usage"] = "Укажи ставку: <code>/darts bet 50</code>",
            ["bet.accepted"] = "Ставка {0} принята. Теперь кинь дротик 🎯\nВыплаты: 4→x2, 5→x3, 6 (в яблочко)→x6",
            ["bet.invalid"] = "Неверная сумма ставки",
            ["bet.not_enough"] = "Недостаточно монет (баланс: {0})",
            ["bet.already_pending"] = "У тебя уже есть ставка {0} в этом чате — кинь дротик 🎯",
            ["bet.failed"] = "Не удалось принять ставку",
            ["throw.no_bet"] = "Сначала сделай ставку: <code>/darts bet &lt;сумма&gt;</code>",
            ["throw.win"] = "Выпало <b>{0}</b> — x{1}! Ты забираешь <b>{2}</b> монет. Баланс: {3}",
            ["throw.lose"] = "Выпало <b>{0}</b> — увы, твоя ставка {1} сгорела. Баланс: {2}",
        }),
    ];
}

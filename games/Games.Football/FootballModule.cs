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
            ["usage"] = "Используй: <code>/football bet &lt;сумма&gt;</code>, затем кинь мяч ⚽",
            ["bet.usage"] = "Укажи ставку: <code>/football bet 50</code>",
            ["bet.accepted"] = "Ставка {0} принята. Теперь кинь мяч ⚽\nВыплаты: 4→x2, 5→x2",
            ["bet.invalid"] = "Неверная сумма ставки",
            ["bet.not_enough"] = "Недостаточно монет (баланс: {0})",
            ["bet.already_pending"] = "У тебя уже есть ставка {0} в этом чате — кинь мяч ⚽",
            ["bet.failed"] = "Не удалось принять ставку",
            ["throw.no_bet"] = "Сначала сделай ставку: <code>/football bet &lt;сумма&gt;</code>",
            ["throw.win"] = "Выпало <b>{0}</b> — x{1}! Ты забираешь <b>{2}</b> монет. Баланс: {3}",
            ["throw.lose"] = "Выпало <b>{0}</b> — увы, твоя ставка {1} сгорела. Баланс: {2}",
        }),
    ];
}

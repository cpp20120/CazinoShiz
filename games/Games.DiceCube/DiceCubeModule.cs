using BotFramework.Sdk;

namespace Games.DiceCube;

public sealed class DiceCubeModule : IModule
{
    public string Id => "dicecube";
    public string DisplayName => "🎲 Куб";
    public string Version => "1.0.0";

    public void ConfigureServices(IModuleServiceCollection services)
    {
        services
            .BindOptions<DiceCubeOptions>(DiceCubeOptions.SectionName)
            .AddScoped<IDiceCubeService, DiceCubeService>()
            .AddScoped<IDiceCubeBetStore, DiceCubeBetStore>()
            .AddHandler<DiceCubeHandler>();
    }

    public IModuleMigrations GetMigrations() => new DiceCubeMigrations();

    public IReadOnlyList<BotCommand> GetBotCommands() =>
    [
        new BotCommand("/dice", "dicecube.cmd.dice"),
    ];

    public IReadOnlyList<LocaleBundle> GetLocales() =>
    [
        new LocaleBundle("ru", new Dictionary<string, string>
        {
            ["display_name"] = "Куб",
            ["cmd.dice"] = "Поставить на кубик",
            ["usage"] = "Используй: <code>/dice bet &lt;сумма&gt;</code>, затем брось кубик 🎲",
            ["bet.usage"] = "Укажи ставку: <code>/dice bet 50</code>",
            ["bet.accepted"] = "Ставка {0} принята. Теперь кинь кубик 🎲\nВыплаты: 4→x2, 5→x3, 6→x5",
            ["bet.invalid"] = "Неверная сумма ставки",
            ["bet.not_enough"] = "Недостаточно монет (баланс: {0})",
            ["bet.already_pending"] = "У тебя уже есть ставка {0} в этом чате — брось кубик 🎲",
            ["bet.failed"] = "Не удалось принять ставку",
            ["roll.no_bet"] = "Сначала сделай ставку: <code>/dice bet &lt;сумма&gt;</code>",
            ["roll.win"] = "Выпало <b>{0}</b> — x{1}! Ты забираешь <b>{2}</b> монет. Баланс: {3}",
            ["roll.lose"] = "Выпало <b>{0}</b> — увы, твоя ставка {1} сгорела. Баланс: {2}",
        }),
    ];
}

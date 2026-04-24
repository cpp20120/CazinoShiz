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
            ["usage"] = "🎲 <b>Куб</b>\n"
                + "<code>/dice</code> или <code>/dice bet</code> — ставка по умолчанию <b>{0}</b> (те же правила, что и при явной сумме).\n"
                + "<code>/dice bet &lt;сумма&gt;</code> — своя ставка. Справка: <code>/dice help</code>",
            ["bet.usage"] = "Неверная сумма. Примеры: <code>/dice</code>, <code>/dice bet</code> (={0}) или <code>/dice bet 50</code>",
            ["bet.accepted"] = "Ставка {0} принята. Сейчас бот кинет 🎲 — свой 🎲 не отправляй.\nВыплаты: 4→x{1}, 5→x{2}, 6→x{3}",
            ["roll.wait_bot"] = "Эта ставка ждёт бросок <b>от бота</b>. Свой 🎲 не отправляй — один бросок на ставку.",
            ["bet.cooldown"] = "Погоди {0} с — слишком частые ставки в чате.",
            ["bet.invalid"] = "Неверная сумма ставки",
            ["bet.not_enough"] = "Недостаточно монет (баланс: {0})",
            ["bet.already_pending"] = "У тебя уже есть ставка {0} в этом чате — дождись броска бота 🎲",
            ["bet.busy_other"] = "Сначала дождись броска бота в {0} — в этом чате только одна активная мини-игра.",
            ["bet.daily_roll_limit"] = "Лимит бросков кубика на сегодня исчерпан ({0}/{1}). Попробуй завтра. 🎲",
            ["bet.failed"] = "Не удалось принять ставку",
            ["roll.no_bet"] = "Сначала сделай ставку: <code>/dice</code>, <code>/dice bet</code> или <code>/dice bet &lt;сумма&gt;</code>.",
            ["roll.win"] = "Выпало <b>{0}</b> — x{1}. Ставка: {2} · выплата: <b>{3}</b> · чистыми: <b>+{4}</b>. Баланс: {5}",
            ["roll.lose"] = "Выпало <b>{0}</b>. <b>Проигрыш: −{1}</b> монет (ставка сгорела). Баланс: {2}",
        }),
    ];
}

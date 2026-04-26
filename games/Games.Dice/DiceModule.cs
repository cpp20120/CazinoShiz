// ─────────────────────────────────────────────────────────────────────────────
// DiceModule — the pilot proving the framework's composition surface works
// end-to-end. Exercises:
//
//   • BindOptions  — Games:dice section bound into DiceOptions
//   • AddScoped    — the application service + history-store
//   • AddHandler   — UpdateRouter picks up [MessageDice("🎰")] reflectively
//   • GetMigrations — per-module schema for dice_rolls
//   • GetLocales   — Russian strings keyed under "dice.*" in the host localizer
//
// Intentionally NOT exercised: RegisterAggregate (stateless), AddProjection
// (no read models), AddAdminPage/AddBackgroundJob/AddCommandHandler (none
// needed for this game). Those paths are proven by other modules during #15.
// ─────────────────────────────────────────────────────────────────────────────

using BotFramework.Sdk;

namespace Games.Dice;

public sealed class DiceModule : IModule
{
    public string Id => "dice";
    public string DisplayName => "🎰 Слоты";
    public string Version => "1.0.0";

    public void ConfigureServices(IModuleServiceCollection services)
    {
        services
            .BindOptions<DiceOptions>(DiceOptions.SectionName)
            .AddScoped<IDiceService, DiceService>()
            .AddScoped<IDiceHistoryStore, DiceHistoryStore>()
            .AddHandler<DiceHandler>();
    }

    public IModuleMigrations GetMigrations() => new DiceMigrations();

    public IReadOnlyList<LocaleBundle> GetLocales() =>
    [
        new LocaleBundle("ru", new Dictionary<string, string>
        {
            ["display_name"] = "Слоты",
            ["err.forwarded"] = "Не обманывай меня! 😠",
            ["err.not_enough_coins"] = "Кажется, у кого-то закончились монеты. 😢\nКрутить барабан стоит {0} монет.",
            ["err.daily_roll_limit"] = "Лимит бросков кубика на сегодня исчерпан ({0}/{1}). Попробуй завтра. 🎲",
            ["result.win"] = "Поздравляю, ты выиграл <i>{0} - {1} (ставка) = <b>{2} монет</b></i>! 🎉",
            ["result.lose"] = "Ой-ой, сегодня удача не на твоей стороне. Ты потерял <i>{0} - {1} (компенсация) = <b>{2} монет</b></i> 💸",
            ["result.balance"] = "Твой баланс: <b>{0} монет</b>",
            ["result.gas"] = "<i>Кстати, за эту операцию сняли ещё {0} монет</i>",
        }),
    ];
}

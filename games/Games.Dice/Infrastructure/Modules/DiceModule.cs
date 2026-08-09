// ─────────────────────────────────────────────────────────────────────────────
// DiceModule — the pilot proving the framework's composition surface works
// end-to-end. Exercises:
//
//   • BindOptions  — Games:dice section bound into DiceOptions
//   • AddScoped    — compatibility facade + atomic action/state/record adapters
//   • AddHandler   — UpdateRouter picks up [MessageDice("🎰")] reflectively
//   • GetMigrations — per-module schema for dice_rolls
//   • GetLocales   — Russian strings keyed under "dice.*" in the host localizer
//
// Intentionally NOT exercised: RegisterAggregate (stateless), AddProjection
// (no read models), AddAdminPage/AddBackgroundJob/AddCommandHandler (none
// needed for this game). Those paths are proven by other modules during #15.
// ─────────────────────────────────────────────────────────────────────────────


namespace Games.Dice.Infrastructure.Modules;

using BotFramework.Contracts.Messaging;
using Games.Dice.Application.Requests;
using Games.Dice.Contracts.Play;
using Games.Dice.Infrastructure.Messaging;
using Games.Dice.Application.Execution;
using BotFramework.Host.Execution;
using BotFramework.Host.Wagering;
using BotFramework.Sdk.Execution;
using Games.Dice.Application.Wagering;
using Games.Dice.Infrastructure.Configuration;

public sealed class DiceModule : IModule
{
    public string Id => "dice";
    public string DisplayName => "🎰 Слоты";
    public string Version => "1.0.0";

    public void ConfigureServices(IModuleServiceCollection services)
    {
        services
            .BindOptions<DiceOptions, DiceOptionsValidator>(DiceOptions.SectionName)
            .AddScoped<IDiceService, DiceService>()
            .AddScoped<IGameAction<DiceCommand, NoGameState, DicePlayResult>, DiceAction>()
            .AddScoped<GameExecutionDescriptor<DiceCommand, NoGameState, DicePlayResult>, DiceExecutionDescriptor>()
            .AddScoped<IGameStateStore<DiceCommand, NoGameState>, DiceStateStore>()
            .AddScoped<IGameRecordWriter, DiceRollRecordWriter>()
            .AddScoped<IRequestHandler<DicePlayRequest, DicePlayResponse>, DicePlayRequestHandler>()
            .AddScoped<MediatR.IRequestHandler<DicePlayRequest, DicePlayResponse>, DicePlayRequestHandler>()
            .AddScoped<IDiceClient, InProcessDiceClient>()
            .AddScoped<IWagerGameResolver, DiceWagerResolver>()
            .AddScoped<IWagerPayoutPolicy, DiceWagerPayoutPolicy>();
    }

    public IModuleMigrations GetMigrations() => new DiceMigrations();

    public IReadOnlyList<LocaleBundle> GetLocales() =>
    [
        new LocaleBundle("ru", new Dictionary<string, string>
(StringComparer.Ordinal) {
            ["display_name"] = "Слоты",
            ["err.forwarded"] = "Не обманывай меня! 😠",
            ["err.not_enough_coins"] = "Кажется, у кого-то закончились монеты. 😢\nКрутить барабан стоит {0} монет.",
            ["err.daily_roll_limit"] = "Лимит спинов слотов на сегодня исчерпан ({0}/{1}). Попробуй завтра. 🎰",
            ["result.win"] = "Поздравляю, ты выиграл <i>{0} - {1} (ставка) = <b>{2} монет</b></i>! 🎉",
            ["result.lose"] = "Ой-ой, сегодня удача не на твоей стороне. Ты потерял <i>{0} - {1} (компенсация) = <b>{2} монет</b></i> 💸",
            ["result.balance"] = "Твой баланс: <b>{0} монет</b>",
            ["result.gas"] = "<i>Кстати, за эту операцию сняли ещё {0} монет</i>",
            ["result.daily_roll_remaining"] = "Осталось попыток <b>{0}/{1}</b>",
        }),
    ];
}

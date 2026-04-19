using BotFramework.Sdk;

namespace Games.Blackjack;

public sealed class BlackjackModule : IModule
{
    public string Id => "blackjack";
    public string DisplayName => "🃏 Блэкджек";
    public string Version => "1.0.0";

    public void ConfigureServices(IModuleServiceCollection services)
    {
        services
            .BindOptions<BlackjackOptions>(BlackjackOptions.SectionName)
            .AddScoped<IBlackjackService, BlackjackService>()
            .AddScoped<IBlackjackHandStore, BlackjackHandStore>()
            .AddHandler<BlackjackHandler>()
            .AddBackgroundJob<BlackjackHandTimeoutJob>();
    }

    public IModuleMigrations GetMigrations() => new BlackjackMigrations();

    public IReadOnlyList<BotCommand> GetBotCommands() =>
    [
        new BotCommand("/blackjack", "blackjack.cmd.blackjack"),
    ];

    public IReadOnlyList<LocaleBundle> GetLocales() =>
    [
        new LocaleBundle("ru", new Dictionary<string, string>
        {
            ["display_name"] = "Блэкджек",
            ["cmd.blackjack"] = "Сыграть в блэкджек",
            ["usage"] = "🃏 <b>Блэкджек</b>\n\n/blackjack <i>ставка</i> — начать раздачу (от {0} до {1}).\nКнопки: Ещё / Стоп / Удвоить.",
            ["render.header"] = "🃏 <b>Блэкджек</b> — ставка: {0}",
            ["render.dealer"] = "Дилер: {0}{1}",
            ["render.player"] = "Ты: {0} ({1})",
            ["render.balance"] = "Баланс: {0}",
            ["btn.hit"] = "🃏 Ещё",
            ["btn.stand"] = "✋ Стоп",
            ["btn.double"] = "💰 Удвоить",
            ["outcome.player_blackjack"] = "🎉 Блэкджек! Выигрыш: +{0}",
            ["outcome.player_win"] = "✅ Победа! Выигрыш: +{0}",
            ["outcome.dealer_bust"] = "💥 Дилер перебрал! Выигрыш: +{0}",
            ["outcome.player_bust"] = "💀 Перебор. Потеря: -{1}",
            ["outcome.dealer_win"] = "😞 Дилер победил. Потеря: -{1}",
            ["outcome.push"] = "🤝 Ничья. Ставка возвращена.",
            ["err.invalid_bet"] = "Ставка должна быть от {0} до {1} монет.",
            ["err.not_enough_coins"] = "Не хватает монет для этой ставки.",
            ["err.hand_in_progress"] = "Раздача уже идёт — сначала доиграй её.",
            ["err.no_active_hand"] = "Нет активной раздачи. /blackjack <i>ставка</i>",
            ["err.cannot_double"] = "Удвоить можно только на первом действии.",
            ["err.generic"] = "Ошибка.",
        }),
    ];
}

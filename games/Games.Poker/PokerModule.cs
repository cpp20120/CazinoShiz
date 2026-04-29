using BotFramework.Sdk;

namespace Games.Poker;

public sealed class PokerModule : IModule
{
    public string Id => "poker";
    public string DisplayName => "🃏 Покер";
    public string Version => "1.0.0";

    public void ConfigureServices(IModuleServiceCollection services)
    {
        services
            .BindOptions<PokerOptions>(PokerOptions.SectionName)
            .AddScoped<IPokerService, PokerService>()
            .AddScoped<IPokerTableStore, PokerTableStore>()
            .AddScoped<IPokerSeatStore, PokerSeatStore>()
            .AddHandler<PokerHandler>()
            .AddBackgroundJob<PokerTurnTimeoutJob>();
    }

    public IModuleMigrations GetMigrations() => new PokerMigrations();

    public IReadOnlyList<BotCommand> GetBotCommands() =>
    [
        new BotCommand("/poker", "poker.cmd.poker"),
    ];

    public IReadOnlyList<LocaleBundle> GetLocales() =>
    [
        new LocaleBundle("ru", new Dictionary<string, string>
        {
            ["display_name"] = "Покер",
            ["cmd.poker"] = "Сыграть в Техасский Холдем",

            ["usage"] = "🃏 <b>Покер (Техасский Холдем)</b>\n\n"
                + "/poker create — создать стол в этой группе\n"
                + "/poker join — присоединиться к столу группы\n"
                + "/poker start — начать раздачу (хост)\n"
                + "/poker leave — выйти из-за стола\n"
                + "/poker status — показать текущий стол\n"
                + "/poker raise <i>сумма</i> — повысить ставку\n\n"
                + "Используй кнопки во время раздачи.",

            ["created"] = "🃏 Стол создан. Код: <code>{0}</code> · бай-ин {1}.\nЖми «Сесть» или пиши <code>/poker join</code>. Когда все сядут — <code>/poker start</code>.",
            ["joined"] = "✅ Сел за стол <code>{0}</code>. Игроков: {1}/{2}.",
            ["left"] = "Ты вышел из-за стола.",
            ["table_closed"] = "Стол закрыт — в нём никого не осталось.",

            // state.* — rendered by PokerStateRenderer
            ["state.header"] = "Стол {0}",
            ["state.community"] = "Общие: {0}",
            ["state.pot_bet"] = "Банк: <b>{0}</b> · Ставка: <b>{1}</b>",
            ["state.waiting_for_start"] = "Ожидаем начала раздачи. Хост — нажми /poker start.",
            ["state.your_cards"] = "Твои карты: <b>{0}</b>",
            ["state.to_call"] = "Чтобы уравнять: <b>{0}</b>",
            ["state.can_check"] = "Можно чекать.",

            ["seat.folded"] = "фолд",
            ["seat.allin"] = "олл-ин",
            ["seat.sitting_out"] = "не играет",
            ["seat.bet"] = "ставка {0}",
            ["seat.you"] = "ты",

            ["showdown.header"] = "Шоудаун",

            ["phase.seating"] = "ожидание",
            ["phase.preflop"] = "префлоп",
            ["phase.flop"] = "флоп",
            ["phase.turn"] = "тёрн",
            ["phase.river"] = "ривер",
            ["phase.showdown"] = "шоудаун",

            ["btn.check"] = "✅ Чек",
            ["btn.call"] = "📞 Колл {0}",
            ["btn.fold"] = "❌ Фолд",
            ["btn.raise"] = "💰 Рейз",
            ["btn.allin"] = "🔥 Олл-ин",
            ["btn.join"] = "🪑 Сесть",
            ["btn.start"] = "▶️ Старт",
            ["btn.cards"] = "👀 Мои карты",
            ["btn.raise_amount"] = "Рейз {0}",
            ["btn.allin_amount"] = "Олл-ин {0}",

            ["raise_menu.prompt"] = "Выбери сумму (мин. {0}, макс. {1}). Или текстом: <code>/poker raise N</code>",
            ["cards.yours"] = "Твои карты: {0}",
            ["cards.none"] = "Сейчас у тебя нет закрытых карт.",

            ["auto.fold"] = "⏱️ {0} не успел — авто-фолд.",
            ["auto.check"] = "⏱️ {0} не успел — авто-чек.",

            ["err.only_group"] = "Покер запускается в группе. Добавь бота в чат и напиши /poker create.",
            ["err.join_missing_code"] = "Укажи код: /poker join КОД",
            ["err.raise_missing_amount"] = "Укажи сумму: /poker raise СУММА",
            ["err.not_enough_coins"] = "Не хватает монет. Бай-ин: {0}.",
            ["err.already_seated"] = "Ты уже сидишь за столом. Сначала выйди: /poker leave.",
            ["err.table_not_found"] = "Такого стола нет или он закрыт.",
            ["err.table_full"] = "Стол заполнен.",
            ["err.hand_in_progress"] = "Раздача уже идёт — дождись её окончания.",
            ["err.not_host"] = "Только хост может начать раздачу.",
            ["err.need_two"] = "Нужно минимум два игрока со стеком.",
            ["err.no_table"] = "Ты сейчас не за столом.",
            ["err.not_your_turn"] = "Сейчас не твой ход.",
            ["err.action_for_other_player"] = "Эта кнопка для игрока, чей сейчас ход.",
            ["err.cannot_check"] = "Сейчас нельзя чекать — надо колл или фолд.",
            ["err.raise_too_small"] = "Рейз слишком маленький.",
            ["err.raise_too_large"] = "Рейз больше твоего стека.",
            ["err.table_already_exists"] = "В этой группе уже есть открытый покерный стол.",
            ["err.invalid_action"] = "Неверное действие.",
            ["err.temporary_failure"] = "Покер временно занят или база не ответила. Попробуй ещё раз через пару секунд.",
        }),
    ];
}

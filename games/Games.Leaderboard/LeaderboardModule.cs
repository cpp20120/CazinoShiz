using BotFramework.Sdk;

namespace Games.Leaderboard;

public sealed class LeaderboardModule : IModule
{
    public string Id => "leaderboard";
    public string DisplayName => "🏆 Топ";
    public string Version => "1.0.0";

    public void ConfigureServices(IModuleServiceCollection services)
    {
        services
            .BindOptions<LeaderboardOptions>(LeaderboardOptions.SectionName)
            .AddScoped<ILeaderboardStore, LeaderboardStore>()
            .AddScoped<ILeaderboardService, LeaderboardService>()
            .AddHandler<LeaderboardHandler>();
    }

    public IReadOnlyList<BotCommand> GetBotCommands() =>
    [
        new BotCommand("/top", "leaderboard.cmd.top"),
        new BotCommand("/balance", "leaderboard.cmd.balance"),
        new BotCommand("/daily", "leaderboard.cmd.daily"),
        new BotCommand("/help", "leaderboard.cmd.help"),
    ];
    // /topall is intentionally NOT advertised in the BotFather menu — it's
    // admin-only and gated to private chats by the handler.

    public IReadOnlyList<LocaleBundle> GetLocales() =>
    [
        new LocaleBundle("ru", new Dictionary<string, string>
        {
            ["display_name"] = "Топ",
            ["cmd.top"] = "Топ игроков",
            ["cmd.balance"] = "Мой баланс",
            ["cmd.daily"] = "Ежедневный бонус (малый % от баланса)",
            ["cmd.help"] = "Помощь",

            ["top.header"] = "🏆 <b>Топ игроков</b>",
            ["top.empty"] = "Опа, в топе никого! Похоже никто не крутил последнее время!",
            ["top.truncated"] = "…\n(используй /top full чтобы показать всех)",
            ["top.hidden_reminder"] = "\n<i>Игроки, не заходившие несколько дней, скрыты из топа.</i>",

            ["topall.private_only"] = "🚫 <b>/topall</b> работает только в личке с ботом.",
            ["topall.not_admin"] = "🚫 <b>/topall</b> доступна только администратору бота.",
            ["topall.empty"] = "В базе пусто — ни одного активного игрока ни в одном чате.",
            ["topall.header"] = "🌍 <b>Глобальный топ</b> (сумма по всем чатам, активных игроков: {0})",
            ["topall.truncated"] = "…\n(используй <code>/topall full</code> чтобы показать всех)",
            ["topall.hidden_reminder"] = "\n<i>Игроки, не заходившие несколько дней, скрыты из топа. В скобках — в скольких чатах учтён баланс.</i>",
            ["topall.split.header"] = "🌍 <b>Топ по чатам</b> (всего чатов: {0})",
            ["topall.split.chat_header"] = "📍 <b>{0}</b> · <i>{1}</i> · <code>{2}</code>",
            ["topall.split.private_label"] = "ЛС {0}",
            ["topall.split.unknown_label"] = "Чат {0}",
            ["topall.split.hint_full"] = "<i>Показано до 5 игроков на чат. Используй <code>/topall split full</code> для полного списка.</i>",

            ["balance.visible"] = "💰 Твой баланс: <b>{0}</b>",
            ["balance.hidden"] = "💰 Твой баланс: <b>{0}</b>\n<i>Ты скрыт из топа из-за неактивности.</i>",

            ["daily.claimed"] = "🎁 Ежедневный бонус: <b>+{0}</b> монет (крошка от баланса, с потолком). Баланс: <b>{1}</b>.",
            ["daily.already"] = "Сегодня бонус уже получен. Загляни завтра!",
            ["daily.disabled"] = "Ежедневный бонус выключен.",
            ["daily.empty_balance"] = "С нулевого баланса бонус не начислится — сначала поиграй.",
            ["daily.too_small"] = "Мало, чтобы сделать хоть 1 монету (подними баланс). Можно снова написать /daily сегодня — попытка не сожгла день.",
            ["daily.failed"] = "Не удалось начислить ежедневный бонус. Попробуй ещё раз чуть позже.",

            ["help"] = "🎰 <b>CasinoShiz</b>\n\n"
                + "/top — таблица лидеров\n"
                + "/balance — твой баланс\n"
                + "/daily — маленький ежедневный бонус (процент с потолком)\n"
                + "/transfer — перевод монет игроку в группе (см. <code>/transfer</code>)\n"
                + "/redeem <i>код</i> — активировать код\n\n"
                + "Игры: /sh · /poker · /blackjack · /horse · /dice · /darts · /football · /basket · /bowling\n"
                + "Слоты: отправь 🎰 в чат\n\n"
                + "Куб / дартс / футбол / баскет / боулинг: голая команда (<code>/dice</code>, <code>/darts</code>, …) или <code>… bet</code> <b>без суммы</b> — ставка по умолчанию: 🎲 <b>{0}</b>, 🎯 <b>{1}</b>, ⚽ <b>{2}</b>, 🏀 <b>{3}</b>, 🎳 <b>{4}</b> (как при явной сумме). Справка: <code>/dice help</code> и т.д.\n\n"
                + "🐎 Скачки — правила: /horse help"
        }),
    ];
}

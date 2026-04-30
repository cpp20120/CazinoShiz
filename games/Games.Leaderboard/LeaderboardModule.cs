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

            // {0}=diceDef {1}=dartsDef {2}=footballDef {3}=basketDef {4}=bowlingDef
            // {5}=pickDef {6}=dailyTicketPrice {7}=dailyDrawHour {8}=utcOffsetText (e.g. "+7")
            ["help"] = "🎰 <b>CasinoShiz</b>\n\n"

                + "<b>💰 Экономика</b>\n"
                + "• /balance — твой баланс\n"
                + "• /daily — ежедневный бонус (процент от баланса с потолком)\n"
                + "• /top — топ игроков чата (<code>/top full</code> — без обрезки)\n"
                + "• /transfer — перевод монет другому игроку (см. <code>/transfer</code>)\n"
                + "• /redeem <i>код</i> — активировать код\n\n"

                + "<b>🎲 Telegram-кубики</b>\n"
                + "Голая команда даёт ставку по умолчанию: "
                + "🎲 <b>{0}</b> · 🎯 <b>{1}</b> · ⚽ <b>{2}</b> · 🏀 <b>{3}</b> · 🎳 <b>{4}</b>\n"
                + "• /dice — слоты (5️⃣ кубик/слоты)\n"
                + "• /darts — дартс\n"
                + "• /football — футбол\n"
                + "• /basket — баскетбол\n"
                + "• /bowling — боулинг\n"
                + "• 🎰 (отправь эмодзи в чат) — слоты на месте\n"
                + "<i>Справка по любой: <code>/dice help</code>, <code>/darts help</code> и т.д.</i>\n\n"

                + "<b>🃏 Большие игры</b>\n"
                + "• /poker — покер на нескольких\n"
                + "• /blackjack — блэкджек против бота\n"
                + "• /sh — Secret Hitler (политическая игра)\n"
                + "• /horse — скачки в чате (правила: <code>/horse help</code>)\n\n"

                + "<b>⚔ PvP</b>\n"
                + "• /challenge — вызвать игрока на дуэль (см. <code>/challenge</code>)\n\n"

                + "<b>🎯 Pick &amp; лотереи</b>\n"
                + "• /pick — ставка на исход из своих вариантов (по умолчанию <b>{5}</b>).\n"
                + "  Поддержка: явный выбор, парлей, серия побед, удвоение ×2.\n"
                + "  Примеры: <code>/pick amogus, aboba</code> · "
                + "<code>/pick aboba | amogus, aboba, sus</code> · <code>/pick help</code>\n"
                + "• /picklottery <i>ставка</i> + /pickjoin — быстрая 5-минутная лотерея в чате\n"
                + "• /dailylottery — суточная лотерея чата (билет <b>{6}</b> монет, "
                + "розыгрыш в <b>{7}:00 (UTC{8})</b>). Шорткат: <code>/dailylottery N</code> = купить N билетов.\n\n"

                + "<b>🖼 Прочее</b>\n"
                + "• /pixelbattle — общий пиксель-холст (мини-приложение)\n\n"

                + "<i>Подсказка: у любой команды с аргументами есть <code>help</code> — "
                + "<code>/transfer help</code>, <code>/pick help</code>, и т.д.</i>"
        }),
    ];
}

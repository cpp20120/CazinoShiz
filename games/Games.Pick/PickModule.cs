using BotFramework.Sdk;

namespace Games.Pick;

public sealed class PickModule : IModule
{
    public string Id => "pick";
    public string DisplayName => "🎯 Выбор";
    public string Version => "2.0.0";

    public void ConfigureServices(IModuleServiceCollection services)
    {
        services
            .BindOptions<PickOptions>(PickOptions.SectionName)

            // single-player /pick
            .AddSingleton<PickStreakStore>()
            .AddSingleton<PickChainStore>()
            .AddScoped<IPickService, PickService>()
            .AddHandler<PickHandler>()

            // multi-user 5-min lottery
            .AddSingleton<IPickLotteryStore, PickLotteryStore>()
            .AddSingleton<IPickLotteryService, PickLotteryService>()
            .AddHandler<PickLotteryHandler>()
            .AddBackgroundJob<PickLotterySweeperJob>()

            // per-chat daily lottery
            .AddSingleton<IPickDailyLotteryStore, PickDailyLotteryStore>()
            .AddSingleton<IPickDailyLotteryService, PickDailyLotteryService>()
            .AddHandler<PickDailyLotteryHandler>()
            .AddBackgroundJob<PickDailyLotterySweeperJob>();
    }

    public IModuleMigrations GetMigrations() => new PickMigrations();

    public IReadOnlyList<BotCommand> GetBotCommands() =>
    [
        new BotCommand("/pick", "pick.cmd.pick"),
        new BotCommand("/picklottery", "pick.cmd.picklottery"),
        new BotCommand("/pickjoin", "pick.cmd.pickjoin"),
        new BotCommand("/dailylottery", "pick.cmd.dailylottery"),
    ];

    public IReadOnlyList<LocaleBundle> GetLocales() =>
    [
        new LocaleBundle("ru", new Dictionary<string, string>
        {
            ["display_name"] = "Выбор",
            ["cmd.pick"] = "Сделай ставку на вариант — бот крутит",
            ["cmd.picklottery"] = "Открыть/просмотреть быструю лотерею в чате",
            ["cmd.pickjoin"] = "Войти в открытую быструю лотерею",
            ["cmd.dailylottery"] = "Купить билет суточной лотереи (розыгрыш в полночь)",

            // ── /pick usage ───────────────────────────────────────────────────
            // {0}=DefaultBet, {1}=MinVariants, {2}=MaxVariants, {3}=MaxBet, {4}=HouseEdgePercent
            ["usage"] =
                "🎯 <b>/pick</b> — ставка на исход.\n"
                + "Бот случайно выбирает один из вариантов. Если выпал твой — выплата с учётом коэффициента "
                + "и небольшой комиссии (<b>{4}%</b>). Чем шире сеть — тем меньше выигрыш на удачу.\n\n"
                + "<b>Базовый синтаксис</b>\n"
                + "• <code>/pick amogus, aboba</code> — ставка <b>{0}</b>, бэк на первый\n"
                + "• <code>/pick 50 amogus, aboba, sus</code> — ставка 50, бэк на первый\n"
                + "• <code>/pick bet 50 amogus, aboba</code> — то же явно\n\n"
                + "<b>Явный выбор (через <code>|</code>)</b>\n"
                + "• <code>/pick aboba | amogus, aboba, sus</code> — бэк по имени\n"
                + "• <code>/pick 2 | amogus, aboba, sus</code> — бэк по позиции (1..N)\n"
                + "• <code>/pick 50 aboba | amogus, aboba, sus</code> — ставка 50 + бэк по имени\n\n"
                + "<b>Парлей (несколько вариантов)</b>\n"
                + "• <code>/pick a, c | a, b, c, d</code> — бэк сразу на a и c\n"
                + "  Выплата: <code>ставка × N/k × (1−комиссия)</code>\n\n"
                + "<b>Серия побед</b>: за каждую следующую победу подряд бонус сверху.\n"
                + "<b>Удвоение (×2)</b>: после победы можно нажать кнопку — снова крутишь весь выигрыш.\n\n"
                + "Лотерея: <code>/picklottery 100</code>, <code>/pickjoin</code>.\n\n"
                + "<i>Вариантов от {1} до {2}. Максимальная ставка: {3}.</i>",

            // ── /pick errors ──────────────────────────────────────────────────
            ["err.generic"] = "Что-то пошло не так. Попробуй ещё раз.",
            ["err.too_few"] = "Нужно минимум <b>{0}</b> вариантов через запятую.",
            ["err.too_many"] = "Слишком много вариантов. Максимум: <b>{0}</b>.",
            ["err.invalid_bet"] = "Ставка должна быть положительным числом.",
            ["err.invalid_bet_capped"] = "Ставка должна быть от <b>1</b> до <b>{0}</b>.",
            ["err.invalid_choice"] = "Не понял, на что ставишь. Используй имя варианта или его номер (1..N).",
            ["err.no_coins"] = "Не хватает монет на ставку. Баланс: <b>{0}</b>.",
            ["err.no_coins_chain"] = "На удвоение монет не хватает (баланс <b>{0}</b>). Цепочка прервана.",

            // ── reveal animation ──────────────────────────────────────────────
            // {0}=bet, {1}=N, {2}=k
            ["reveal.header"] = "🎲 Бросаю кубик… ставка <b>{0}</b>, вариантов {1}, бэк {2}",
            ["reveal.header_chain"] = "🔥 Удвоение в действии… ставка <b>{0}</b>, вариантов {1}, бэк {2}",

            // ── win lines ─────────────────────────────────────────────────────
            ["ok.win.header"] = "🎯 Бот выбрал: <b>{0}</b> — твой вариант!",
            ["ok.win.chain_header"] = "🔥 Двойная удача! Бот снова на твоей стороне: <b>{0}</b>",
            // {0}=bet, {1}=N, {2}=k, {3}=payout, {4}=net
            ["ok.win.payout"] = "Ставка <b>{0}</b> × ({1}/{2}) − комиссия = <b>{3}</b> (чистыми <b>+{4}</b>)",
            // {0}=streak, {1}=bonus
            ["ok.win.streak"] = "🔥 Серия <b>×{0}</b>: бонус <b>+{1}</b>",
            ["ok.win.streak_no_bonus"] = "Серия <b>×{0}</b> зафиксирована.",
            ["ok.win.balance"] = "Баланс: <b>{0}</b>",
            // {0}=stakeForNext, {1}=hops left
            ["ok.win.chain_offer"] = "👉 Хочешь крутануть весь выигрыш ещё раз? Поставлено <b>{0}</b> на тот же расклад. (осталось хопов: {1})",

            // ── lose lines ────────────────────────────────────────────────────
            ["ok.lose"] = "🎲 Бот выбрал: <b>{0}</b>\nТвоя ставка <b>{1}</b> сгорела (бэк был на: {3}). Баланс: <b>{2}</b>",
            ["ok.lose.chain"] = "💥 Удвоение не зашло. Бот выбрал: <b>{0}</b>\nВесь пот <b>{1}</b> сгорел. Баланс: <b>{2}</b>",
            ["ok.lose.streak_broken"] = "<i>Серия из {0} побед прервана.</i>",

            // ── chain ──────────────────────────────────────────────────────────
            ["chain.button"] = "🔥 Удвоить ×2 ({0})",
            ["chain.expired"] = "Время кнопки истекло.",
            ["chain.not_your_turn"] = "Эта кнопка не для тебя.",
            ["chain.acknowledged"] = "Кручу…",

            // ── /picklottery & /pickjoin ──────────────────────────────────────
            // {0}=MinStake, {1}=MaxStake|∞, {2}=DurationMinutes, {3}=MinEntrants, {4}=FeePct
            ["lottery.usage"] =
                "🎰 <b>/picklottery</b> — лотерея в этом чате.\n\n"
                + "• <code>/picklottery 100</code> — открыть пул со ставкой 100 (открывающий сразу участвует)\n"
                + "• <code>/picklottery info</code> — что сейчас открыто\n"
                + "• <code>/picklottery cancel</code> — отменить (только открывающий, монеты вернутся)\n"
                + "• <code>/pickjoin</code> — войти в открытый пул\n\n"
                + "Ставка: от <b>{0}</b> до <b>{1}</b>. Время: <b>{2}</b> мин.\n"
                + "Кворум: минимум <b>{3}</b> участников. Иначе все ставки возвращаются.\n"
                + "Комиссия дома: <b>{4}%</b> от пота. Победитель забирает остаток.",

            // {0}=stake, {1}=minutesLeft, {2}=feePct, {3}=minEntrants, {4}=balance
            ["lottery.opened"] =
                "🎰 <b>Лотерея открыта!</b>\n"
                + "Ставка: <b>{0}</b> · окно: <b>{1}</b> мин · комиссия: <b>{2}%</b>\n"
                + "Минимум участников: <b>{3}</b>. Заходи: <code>/pickjoin</code>",

            // {0}=stake, {1}=entrants, {2}=pot, {3}=minutesLeft, {4}=opener
            ["lottery.info.open"] =
                "🎰 <b>Лотерея в чате</b>\n"
                + "Ставка: <b>{0}</b> · участников: <b>{1}</b> · пот: <b>{2}</b>\n"
                + "Открыл: {4} · осталось: <b>{3}</b> мин",

            ["lottery.info.none"] = "В этом чате лотерея не открыта. <code>/picklottery 100</code> — открыть.",

            // {0}=name, {1}=stake, {2}=entrants, {3}=potSoFar
            ["lottery.joined"] =
                "✅ {0} в игре! Ставка <b>{1}</b>. Участников: <b>{2}</b>, пот: <b>{3}</b>",

            // ── lottery errors ────────────────────────────────────────────────
            // {0}=MinStake, {1}=MaxStake|∞
            ["lottery.err.invalid_stake"] = "Ставка лотереи: от <b>{0}</b> до <b>{1}</b>.",
            ["lottery.err.no_coins"] = "Не хватает монет на ставку лотереи. Баланс: <b>{0}</b>.",
            ["lottery.err.no_coins_join"] = "Чтобы войти, нужно <b>{0}</b> монет (баланс <b>{1}</b>).",
            // {0}=stake, {1}=minutesLeft
            ["lottery.err.already_open"] =
                "Здесь уже идёт лотерея на <b>{0}</b> монет. Осталось <b>{1}</b> мин. <code>/pickjoin</code> — войти.",
            ["lottery.err.already_open_unknown"] = "Здесь уже идёт лотерея.",
            ["lottery.err.no_open"] = "В этом чате нет открытой лотереи. <code>/picklottery <ставка></code> — открыть.",
            ["lottery.err.already_joined"] = "Ты уже участвуешь в этой лотерее.",
            ["lottery.err.cant_cancel"] = "Отменить нельзя: ты не открывал эту лотерею или её уже разыграли.",

            // ── lottery sweeper messages (public) ─────────────────────────────
            // {0}=entrants, {1}=stake
            ["lottery.cancelled"] = "🎰 Лотерея не собрала кворум (участников: <b>{0}</b>, ставка <b>{1}</b>). Все монеты возвращены.",
            ["lottery.cancelled_by_opener"] = "🎰 Лотерея отменена открывающим. Участников: <b>{0}</b>, ставка <b>{1}</b> — всем возвращено.",
            // {0}=winner (HTML-encoded), {1}=pot, {2}=fee, {3}=payout, {4}=entrants
            ["lottery.settled"] =
                "🎰 <b>Розыгрыш!</b> Победитель: {0}\n"
                + "Пот: <b>{1}</b> · комиссия: <b>{2}</b> · выплата: <b>{3}</b>\n"
                + "Участников было: <b>{4}</b>",

            // ── /dailylottery ──────────────────────────────────────────────────
            // {0}=ticketPrice, {1}=maxPerCommand, {2}=maxPerDay|∞, {3}=feePct, {4}=drawTime (e.g. "18:00 (UTC+7)")
            ["daily.usage"] =
                "🎟 <b>/dailylottery</b> — суточная лотерея в этом чате.\n\n"
                + "• <code>/dailylottery</code> — статус текущего пула\n"
                + "• <code>/dailylottery buy</code> или <code>/dailylottery 1</code> — купить 1 билет\n"
                + "• <code>/dailylottery buy 5</code> или <code>/dailylottery 5</code> — купить 5\n"
                + "• <code>/dailylottery history</code> — последние розыгрыши в чате\n\n"
                + "Цена билета: <b>{0}</b> монет. Максимум за раз: <b>{1}</b>. "
                + "Лимит на игрока в день: <b>{2}</b>.\n"
                + "Розыгрыш каждый день в <b>{4}</b>. "
                + "Победитель выбирается случайно среди всех билетов "
                + "(больше билетов = выше шанс). Комиссия дома: <b>{3}%</b>.",

            // {0}=ticketPrice
            ["daily.info.empty"] =
                "🎟 Сегодня в этом чате ещё никто не купил билет суточной лотереи.\n"
                + "Билет — <b>{0}</b> монет. Открой розыгрыш: <code>/dailylottery 1</code>",

            // {0}=ticketPrice, {1}=dayLocal
            ["daily.info.header"] = "🎟 <b>Суточная лотерея · {1}</b> · билет <b>{0}</b>",
            // {0}=tickets, {1}=distinctUsers, {2}=pot, {3}=timeLeftText
            ["daily.info.stats"] = "Всего билетов: <b>{0}</b> · игроков: <b>{1}</b> · пот: <b>{2}</b> · до розыгрыша: <b>{3}</b>",
            // {0}=viewerTickets, {1}=winChancePct
            ["daily.info.your_tickets"] = "У тебя билетов: <b>{0}</b> · шанс победы: <b>{1}%</b>",
            ["daily.info.top_header"] = "<b>Держатели билетов:</b>",
            // {0}=rank, {1}=name, {2}=count
            ["daily.info.top_row"] = "{0}. {1} — <b>{2}</b>",
            // {0}=ticketPrice
            ["daily.info.buy_hint"] = "<i>Купить ещё: <code>/dailylottery N</code> (по {0} за билет).</i>",

            // {0}=hours, {1}=mins
            ["daily.time.h_m"] = "{0} ч {1} мин",
            // {0}=mins
            ["daily.time.m"] = "{0} мин",

            // {0}=name, {1}=bought, {2}=totalUserTickets, {3}=spent, {4}=totalTickets, {5}=potTotal, {6}=balance
            ["daily.bought"] =
                "🎟 {0} купил <b>{1}</b> билет(ов) (теперь у него <b>{2}</b>).\n"
                + "Списано: <b>{3}</b> · общий пот сегодня: <b>{5}</b> ({4} билетов)\n"
                + "Баланс: <b>{6}</b>",

            // ── daily errors ──────────────────────────────────────────────────
            ["daily.err.invalid_count"] = "Билетов нужно купить хотя бы 1.",
            // {0}=cap
            ["daily.err.over_command_cap"] = "За одну команду можно купить не больше <b>{0}</b> билетов.",
            // {0}=cap, {1}=alreadyOwned
            ["daily.err.over_daily_cap"] = "Лимит на день — <b>{0}</b> билетов. У тебя уже <b>{1}</b>.",
            // {0}=count, {1}=ticketPrice, {2}=totalCost, {3}=balance
            ["daily.err.no_coins"] = "Не хватает монет. {0} × <b>{1}</b> = <b>{2}</b>, а на балансе <b>{3}</b>.",
            ["daily.err.day_closing"] = "День сегодня уже закрылся. Подожди разыгрывания и попробуй завтра.",

            // ── daily history ─────────────────────────────────────────────────
            ["daily.history.empty"] = "В этом чате ещё не было розыгрышей суточной лотереи.",
            ["daily.history.header"] = "🎟 <b>История суточных розыгрышей</b>",
            // {0}=day, {1}=winner, {2}=pot, {3}=payout, {4}=ticketCount
            ["daily.history.row_settled"] = "• <b>{0}</b> — {1} забрал <b>{3}</b> из пота <b>{2}</b> ({4} билетов)",
            // {0}=day
            ["daily.history.row_cancelled"] = "• <b>{0}</b> — никто не участвовал, розыгрыша не было",

            // ── daily sweeper public messages ─────────────────────────────────
            // {0}=day, {1}=winner, {2}=winnerTickets, {3}=ticketsTotal, {4}=distinctUsers, {5}=pot, {6}=fee, {7}=payout
            ["daily.sweep.settled"] =
                "🎟 <b>Розыгрыш суточной лотереи · {0}</b>\n"
                + "🏆 Победитель: {1} ({2}/{3} билетов)\n"
                + "Игроков: <b>{4}</b> · пот: <b>{5}</b> · комиссия: <b>{6}</b> · выплата: <b>{7}</b>",
            // {0}=day, {1}=ticketsTotal
            ["daily.sweep.cancelled"] =
                "🎟 Суточная лотерея за <b>{0}</b> отменена (билетов: {1}). Все ставки возвращены.",
        }),
    ];
}

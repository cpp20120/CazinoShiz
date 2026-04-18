using static CasinoShiz.Helpers.RussianPlural;
using static CasinoShiz.Helpers.ArrayExtensions;

namespace CasinoShiz.Helpers;

public static class Locales
{
    public static string HorsesHelp() =>
        "Иногда мы проводим скачки лошадей, и на них естественно можно поставить!\n" +
        "Соответственно, вот команды которые тебе помогут:\n\n" +
        "/horse bet <i>номер-лошади</i> <i>сумма</i> - ставка на лошадь\n" +
        "/horse result - результат последнего забега, если он был сегодня\n" +
        "/horse info - информация о лошадях и ставках\n";

    public static string TopPlayers() =>
        GetRandom(["🏆 Топ игроков 🏆", "Мажоры нашего казино 🤑", "🥇 Топ игроков 🥇"]);

    public static string TopPlayersFull() => "\n/top full - для показа всех";

    public static string DoNotCheat() =>
        GetRandom([
            "Не пытайся меня обмануть! 😡",
            "Ты думаешь, что я не замечу? 🧐",
            "Не обманывай меня! 😠",
        ]);

    public static string AttemptsLimit(int limit)
    {
        var pluralizedLimit = Plural(limit, ["ставку", "ставки", "ставок"]);
        var pluralizedTimes = Plural(limit, ["раз", "раза", "раз"]);

        return GetRandom([
            $"Лимит ставок на сегодня исчерпан ({limit} {pluralizedLimit}). Попробуй завтра! 🤑",
            $"Сегодня ты уже сделал {limit} {pluralizedLimit}. Попробуй завтра! 🤑",
            $"Я понимаю, что золотая лихорадка в самом разгаре, но ты уже поставил {limit} {pluralizedTimes} сегодня. Попробуй завтра! 🤑",
        ]) + "\n<i>Не забывай, что обновление происходит в полночь</i>";
    }

    public static string NotEnoughCoins(int coins) =>
        $"{GetRandom([
            "А ты думал, что я тебе деньги дам? 😂",
            "Кажется, у кого-то закончились монеты. 😢",
        ])} \nКрутить барабан стоит {coins} монет.";

    public static string Win(int wonCoins, int lostCoins)
    {
        var targetCoins = wonCoins - lostCoins;
        var pluralizedWonCoins = Plural(targetCoins, ["монету", "монеты", "монет"]);

        return GetRandom([
            $"Поздравляю, ты выиграл <i>{wonCoins} - {lostCoins} (ставка) = <b>{targetCoins} {pluralizedWonCoins}</b></i>! 🎉 Наслаждайся своей удачей и продолжай играть, чтобы еще больше увеличить свой капитал!",
            $"Ого, ты сегодня и правда везунчик! Твой выигрыш составил <i>{wonCoins} - {lostCoins} (ставка) = <b>{targetCoins} {pluralizedWonCoins}</b></i>! 💰 Поздравляю с впечатляющим результатом! Наслаждайся игрой и не забывай, что завтра тебе всегда доступно еще больше возможностей!",
            $"Лед тронулся! Ты сорвал куш в размере <i>{wonCoins} - {lostCoins} (ставка) = <b>{targetCoins} {pluralizedWonCoins}</b></i>! 💸 Поздравляю с великолепным выигрышем! Теперь у тебя много вариантов, как потратить свои новые сокровища!",
            $"Господи, удача на тебе улыбается! 😃 Ты выиграл <i>{wonCoins} - {lostCoins} (ставка) = <b>{targetCoins} {pluralizedWonCoins}</b></i> и сделал свой день ярче! Продолжай в том же духе и получай еще больше радости от игры!",
        ]);
    }

    public static string Lose(int lostAmount, int compensation)
    {
        var pluralizedLostAmount = Plural(lostAmount - compensation, ["монету", "монеты", "монет"], true);

        return GetRandom([
            $"Ай-ай-ай, сегодня удача не на твоей стороне! Ты потерял <i>{lostAmount} - {compensation} (компенсация) = <b>{pluralizedLostAmount}</b></i> 💸 Не унывай, в следующий раз обязательно повезет!",
            $"Ой-ой, кажется, сегодня тебе не суждено было победить. Твой банковский баланс стал на <i>{lostAmount} - {compensation} (компенсация) = <b>{pluralizedLostAmount}</b></i> меньше 🙇‍♂️ Но не расстраивайся, у тебя всегда есть возможность вернуться и сорвать большой куш!",
            $"Упс, казино победило сегодня. Ты потерял <i>{lostAmount} - {compensation} (компенсация) = <b>{pluralizedLostAmount}</b></i> в этой игре. Не отчаивайся, следующий расклад обязательно будет в твою пользу!",
        ]);
    }

    public static string BankTax(int tax, int days)
    {
        if (tax == 0) return "\nНалог на ваши сбережения не был применен!";
        if (days > 1)
            return $"\nВы не крутили слоты уже {Plural(days, ["день", "дня", "дней"], true)}! Налог за это время составил {Plural(tax, ["монету", "монеты", "монет"], true)}!";
        return $"\nНалог за сутки хранения составил {Plural(tax, ["монету", "монеты", "монет"], true)}!";
    }

    public static string GasReminder(int gasAmount) =>
        $"<i>Кстати, за эту операцию сняли еще {Plural(gasAmount, ["монету", "монеты", "монет"], true)}</i>";

    public static string YourBalance(int coins) =>
        $"Твой баланс: <b>{Plural(coins, ["монета", "монеты", "монет"], true)}</b>";

    public static string YourBalanceHidden(int daysThreshold) =>
        $"Вы не крутили уже {Plural(daysThreshold, ["день", "дня", "дней"], true)}, поэтому скрыты из топа.\nПокрутите хотя бы раз чтобы снова попасть в топ!";

    public static string HiddenReminder() =>
        "Если Вам кажется что Вы должны быть в топе, а Вас там нет – проверьте что Вы крутили последнее время, или нажмите /balance";

    public static string StakesCreated(int stakesCount)
    {
        if (stakesCount == 0) return "<b>Пока не было ни одной ставки</b>";
        return $"<b>На следующий забег стоит {Plural(stakesCount, ["ставка", "ставки", "ставок"], true)}</b>";
    }

    public static string Koefs(Dictionary<int, double> ks)
    {
        var horseKs = string.Join("", ks.Select(kv =>
        {
            var (safe, value) = GetSafeNumber(kv.Value);
            return $"Лошадь {kv.Key + 1}: <i>{(safe ? $"x{Math.Round(value, 3)}" : "N/A")}</i>\n";
        }));
        return $"<b>Коэффициенты:</b>\n{horseKs}";
    }

    public static string FreespinQuote(string code) =>
        $"\nℹ️ <i>Кстати, кто-то кроме тебя может применить подарочный код, для этого нужно отправить мне в личку \n<code>/redeem {code}</code> (тык),\nи получить круточку бесплатно. \nКто же окажется самым быстрым?</i>";

    public static string FreespinRedeemedQuote() =>
        "\n✅ <i>Тут был подарочный код, но он уже активирован! \nМожет повезет в следующий раз?</i>";

    public static string Help() => string.Join("\n", [
        "Привет! Я чат-бот казино и готов рассказать тебе о правилах наших игр и функциях.",
        "",
        "🎰 В игре \"Слоты\" у нас есть несколько выигрышных комбинаций:",
        "- Если выпадает 3 семерки, ты выиграешь 77 монет.",
        "- Если выпадает 3 лимона, ты выиграешь 30 монет.",
        "- Если выпадает 3 ягоды, ты выиграешь 23 монеты.",
        "- Если выпадает 3 бара, ты получишь 21 монету.",
        "- Если выпадает 2 одинаковых символа, то выигрыш составит 4 + ценности всех значков монет.",
        "- Во всех остальных случаях вы получите компенсацию из суммы ценностей значков.",
        "",
        "Каждая круточка стоит 7 монет (а еще одну заберет бот за свою работу), но выиграть большие суммы оченб легко!",
        "",
        "Каждый день доступно ровно 3 крутки. После того, как ты их использовал, тебе придется подождать до следующего дня, чтобы снова попробовать свою удачу.",
        "Обновление происходит в 00:00. 🕛",
        "Кстати, иногда выпадают подарочные коды с круток других игроков, следи за чатом!",
        "",
        "💰 Чтобы посмотреть топ игроков, просто введи команду /top. Ты увидишь список самых успешных игроков нашего казино.",
        "",
        "🤔 Если у тебя возникнут вопросы или нужна помощь, не стесняйся спросить! Я всегда готов помочь.",
        "",
        "Удачи на наших играх! 🍀",
    ]);

    private static (bool safe, double value) GetSafeNumber(double x)
    {
        if (double.IsNaN(x) || !double.IsFinite(x)) return (false, 0);
        return (true, x);
    }

    public static string PokerUsage() => string.Join("\n", [
        "🃏 <b>Покер (Техасский холдем)</b>",
        "",
        "/poker create - создать стол (в ЛС)",
        "/poker join <i>код</i> - присоединиться",
        "/poker start - хост начинает раздачу",
        "/poker leave - покинуть стол",
        "/poker status - текущее состояние",
        "",
        "Играем только в личке! Бай-ин списывается при присоединении.",
    ]);

    public static string PokerOnlyPrivate() =>
        "Покер только в личных сообщениях бота! Напиши мне в ЛС: /poker create";

    public static string PokerNotEnoughCoins(int buyIn) =>
        $"Для участия нужно {Plural(buyIn, ["монета", "монеты", "монет"], true)} (бай-ин).";

    public static string PokerTableCreated(string code, int buyIn) =>
        $"🃏 Стол создан! Код приглашения: <code>{code}</code>\n" +
        $"Бай-ин: {Plural(buyIn, ["монета", "монеты", "монет"], true)}.\n" +
        $"Пусть остальные войдут: <code>/poker join {code}</code>\n" +
        $"Когда все за столом — /poker start";

    public static string PokerJoined(string code, int seats, int max) =>
        $"Ты за столом <code>{code}</code>. Игроков: {seats}/{max}. Ждём старта от хоста.";

    public static string PokerTableNotFound() => "Стол с таким кодом не найден.";
    public static string PokerTableFull() => "За столом уже нет свободных мест.";
    public static string PokerAlreadySeated() => "Ты уже сидишь за столом. Сначала /poker leave.";
    public static string PokerHandInProgress() => "Раздача уже идёт, подожди следующую.";
    public static string PokerNotHost() => "Только создатель стола может запустить раздачу.";
    public static string PokerNeedTwo() => "Нужно минимум 2 игрока за столом.";
    public static string PokerNoTable() => "Ты не за столом. /poker create чтобы создать новый.";
    public static string PokerNotYourTurn() => "Сейчас не твой ход.";
    public static string PokerInvalidAction() => "Недопустимое действие.";
    public static string PokerLeft() => "Ты покинул стол.";
    public static string PokerTableClosed() => "Стол закрыт.";
    public static string PokerCannotCheck() => "Нельзя чекать — есть ставка.";
    public static string PokerRaiseTooSmall(int min) => $"Минимальная ставка: {min}.";
    public static string PokerRaiseTooLarge(int max) => $"Максимум: {max} (твой стек).";
    public static string PokerAutoFold(string name) => $"⏱ {name} — автофолд по таймауту.";
    public static string PokerAutoCheck(string name) => $"⏱ {name} — автокек по таймауту.";
    public static string PokerWaitingForStart() => "Ожидаем старта раздачи...";
    public static string PokerPhaseName(Data.Entities.PokerPhase phase) => phase switch
    {
        Data.Entities.PokerPhase.PreFlop => "Префлоп",
        Data.Entities.PokerPhase.Flop => "Флоп",
        Data.Entities.PokerPhase.Turn => "Тёрн",
        Data.Entities.PokerPhase.River => "Ривер",
        Data.Entities.PokerPhase.Showdown => "Шоудаун",
        _ => "—",
    };

    public static string BlackjackUsage(int minBet, int maxBet) =>
        $"🃏 <b>Блэкджек</b>\n\n" +
        $"/blackjack <i>ставка</i> — начать раздачу (от {minBet} до {maxBet}).\n" +
        $"Кнопки: Ещё / Стоп / Удвоить.";

    public static string BlackjackInvalidBet(int minBet, int maxBet) =>
        $"Ставка должна быть от {minBet} до {maxBet} монет.";

    public static string BlackjackNotEnoughCoins() => "Не хватает монет для этой ставки.";
    public static string BlackjackHandInProgress() => "Раздача уже идёт — сначала доиграй её.";
    public static string BlackjackNoActiveHand() => "Нет активной раздачи. /blackjack <i>ставка</i>";
    public static string BlackjackCannotDouble() => "Удвоить можно только на первом действии.";

    public static string BlackjackOutcome(Services.Blackjack.BlackjackOutcome outcome, int bet, int payout)
    {
        var net = payout - bet;
        return outcome switch
        {
            Services.Blackjack.BlackjackOutcome.PlayerBlackjack => $"🎉 Блэкджек! Выигрыш: +{net}",
            Services.Blackjack.BlackjackOutcome.PlayerWin => $"✅ Победа! Выигрыш: +{net}",
            Services.Blackjack.BlackjackOutcome.DealerBust => $"💥 Дилер перебрал! Выигрыш: +{net}",
            Services.Blackjack.BlackjackOutcome.PlayerBust => $"💀 Перебор. Потеря: -{bet}",
            Services.Blackjack.BlackjackOutcome.DealerWin => $"😞 Дилер победил. Потеря: -{bet}",
            Services.Blackjack.BlackjackOutcome.Push => "🤝 Ничья. Ставка возвращена.",
            _ => "",
        };
    }
}

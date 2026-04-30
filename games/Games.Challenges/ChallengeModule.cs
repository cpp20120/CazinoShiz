using BotFramework.Sdk;

namespace Games.Challenges;

public sealed class ChallengeModule : IModule
{
    public string Id => "challenges";
    public string DisplayName => "PvP Challenges";
    public string Version => "1.0.0";

    public void ConfigureServices(IModuleServiceCollection services)
    {
        services
            .BindOptions<ChallengeOptions>(ChallengeOptions.SectionName)
            .AddScoped<IChallengeStore, ChallengeStore>()
            .AddScoped<IChallengeService, ChallengeService>()
            .AddHandler<ChallengeHandler>();
    }

    public IModuleMigrations GetMigrations() => new ChallengeMigrations();

    public IReadOnlyList<BotCommand> GetBotCommands() =>
    [
        new BotCommand("/challenge", "challenges.cmd.challenge"),
    ];

    public IReadOnlyList<LocaleBundle> GetLocales() =>
    [
        new LocaleBundle("ru", new Dictionary<string, string>
        {
            ["display_name"] = "PvP Challenges",
            ["cmd.challenge"] = "Вызвать игрока на PvP ставку",
            ["usage"] = "⚔️ <b>PvP Challenge</b>\n"
                + "<code>/challenge @username 500 dice</code>\n"
                + "Или ответь на сообщение игрока: <code>/challenge 500 darts</code>\n"
                + "Игры: <code>dicecube</code> 🎲, <code>darts</code> 🎯, <code>bowling</code> 🎳, <code>basketball</code> 🏀, "
                + "<code>football</code> ⚽, <code>slots</code> 🎰, <code>horse</code> 🐎, <code>blackjack</code> 🃏.",
            ["target.not_found"] = "Не знаю игрока <b>@{0}</b> в этом чате. Пусть сначала напишет боту/сыграет тут, или используй команду ответом на его сообщение.",
            ["created"] = "⚔️ {0} вызывает {1} на <b>{2}</b> монет в <b>{3}</b> {4}\nПобедитель забирает банк минус комиссию 2%.",
            ["accepted.toast"] = "Challenge accepted",
            ["accepted"] = "⚔️ Challenge accepted: {0} vs {1}. Rolling {2}...",
            ["declined"] = "Challenge declined.",
            ["roll.failed"] = "Не смог завершить challenge. Ставки возвращены.",
            ["result.tie"] = "🤝 Ничья: оба выбили <b>{0}</b>. Ставки по <b>{2}</b> возвращены.",
            ["result.win"] = "{0}: <b>{1}</b>\n{2}: <b>{3}</b>\n\n🏆 Победил {4}. Выплата: <b>{5}</b>, комиссия: <b>{6}</b>.",
            ["horse.caption"] = "🐎 PvP horse race\n#1 — {0}\n#2 — {1}",
            ["horse.result"] = "{0}: <b>{1}</b>\n{2}: <b>{3}</b>\n\n🏆 Победил {4}. Выплата: <b>{5}</b>, комиссия: <b>{6}</b>.",
            ["blackjack.tie"] = "🤝 Blackjack push\n{0}: {1}\n{2}: {3}\n\nСтавки по <b>{4}</b> возвращены.",
            ["blackjack.win"] = "🃏 Blackjack duel\n{0}: {1}\n{2}: {3}\n\n🏆 Победил {4}. Выплата: <b>{5}</b>, комиссия: <b>{6}</b>.",
            ["err.amount"] = "Неверная сумма ставки.",
            ["err.self"] = "Нельзя вызвать самого себя.",
            ["err.not_enough"] = "Недостаточно монет для challenge. Баланс: <b>{0}</b>.",
            ["err.pending"] = "Между этими игроками уже есть активный challenge.",
            ["err.not_target"] = "Этот challenge может принять только вызванный игрок.",
            ["err.expired"] = "Challenge истёк.",
            ["err.challenger_poor"] = "У вызвавшего игрока уже не хватает монет. Challenge отменён.",
            ["err.target_poor"] = "У тебя не хватает монет. Challenge отменён.",
            ["err.resolved"] = "Этот challenge уже завершён.",
            ["err.not_found"] = "Challenge не найден.",
        }),
    ];
}

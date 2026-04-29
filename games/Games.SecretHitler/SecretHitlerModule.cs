using BotFramework.Sdk;

namespace Games.SecretHitler;

public sealed class SecretHitlerModule : IModule
{
    public string Id => "sh";
    public string DisplayName => "🗳 Secret Hitler";
    public string Version => "1.0.0";

    public void ConfigureServices(IModuleServiceCollection services)
    {
        services
            .BindOptions<SecretHitlerOptions>(SecretHitlerOptions.SectionName)
            .AddScoped<ISecretHitlerService, SecretHitlerService>()
            .AddScoped<ISecretHitlerGameStore, SecretHitlerGameStore>()
            .AddScoped<ISecretHitlerPlayerStore, SecretHitlerPlayerStore>()
            .AddHandler<SecretHitlerHandler>()
            .AddBackgroundJob<SecretHitlerGateCleanupJob>();
    }

    public IModuleMigrations GetMigrations() => new SecretHitlerMigrations();

    public IReadOnlyList<BotCommand> GetBotCommands() =>
    [
        new BotCommand("/sh", "sh.cmd.sh"),
    ];

    public IReadOnlyList<LocaleBundle> GetLocales() =>
    [
        new LocaleBundle("ru", new Dictionary<string, string>
        {
            ["display_name"] = "Secret Hitler",
            ["cmd.sh"] = "Сыграть в Secret Hitler",

            ["usage"] = "🗳 <b>Secret Hitler</b>\n\n"
                + "/sh create — создать игру\n"
                + "/sh join <i>код</i> — присоединиться\n"
                + "/sh start — начать (хост, ≥5 игроков)\n"
                + "/sh leave — выйти (только в лобби)\n"
                + "/sh status — текущее состояние\n\n"
                + "Используй кнопки во время игры.",

            ["created"] = "🗳 Игра создана. Код: <code>{0}</code> · бай-ин {1}.\nПригласи друзей через <code>/sh join {0}</code>. Когда все соберутся — <code>/sh start</code>.",
            ["joined"] = "✅ Присоединился к игре <code>{0}</code>. Игроков: {1}/{2}.",
            ["left"] = "Ты вышел из игры.",
            ["game_closed"] = "Игра закрыта — никого не осталось.",

            ["board.header"] = "<b>🗳 Secret Hitler</b> · стол <code>{0}</code> · банк <b>{1}</b>",
            ["board.liberals"] = "Либералы: {0} ({1}/{2})",
            ["board.fascists"] = "Фашисты:  {0} ({1}/{2})",
            ["board.election_tracker"] = "Трекер выборов: {0}",
            ["board.phase"] = "Фаза: <b>{0}</b>",
            ["board.president"] = "Президент: <b>{0}</b>",
            ["board.chancellor"] = "Канцлер: <b>{0}</b>",
            ["board.votes"] = "Голосов: <b>{0}/{1}</b>",
            ["board.players"] = "Игроки:",

            ["phase.nomination"] = "Выдвижение канцлера",
            ["phase.election"] = "Голосование",
            ["phase.legislative_president"] = "Президент выбирает карту",
            ["phase.legislative_chancellor"] = "Канцлер принимает закон",
            ["phase.game_end"] = "Игра окончена",

            ["role.liberal"] = "🟦 Либерал",
            ["role.fascist"] = "🟥 Фашист",
            ["role.hitler"] = "🟥 <b>Гитлер</b>",
            ["role.liberal_short"] = "либерал",
            ["role.fascist_short"] = "фашист",
            ["role.hitler_short"] = "Гитлер",
            ["role.your_role"] = "Твоя роль: {0}",
            ["role.your_allies"] = "Твои союзники:",
            ["role.your_fascists"] = "Твои фашисты:",

            ["policy.liberal"] = "🟦 Либерал",
            ["policy.fascist"] = "🟥 Фашист",
            ["policy.enacted_liberal"] = "📜 Принят <b>либеральный</b> закон.",
            ["policy.enacted_fascist"] = "📜 Принят <b>фашистский</b> закон.",

            ["btn.ja"] = "✅ Ja!",
            ["btn.nein"] = "❌ Nein!",
            ["btn.join"] = "➕ Войти",
            ["btn.start"] = "▶️ Старт",
            ["btn.discard"] = "🗑 {0} #{1}",
            ["btn.enact"] = "📜 {0} #{1}",

            ["vote.reveal_header"] = "Результаты голосования:",
            ["vote.ja"] = "✅ Ja",
            ["vote.nein"] = "❌ Nein",
            ["vote.election_passed"] = "Выборы пройдены ({0}:{1}). Канцлер — <b>{2}</b>.",
            ["vote.election_failed"] = "Выборы провалены ({0}:{1}). Трекер выборов: {2}/3.",
            ["vote.hitler_elected_win"] = "⚠️ Гитлер (<b>{0}</b>) избран канцлером при 3+ фашистских законах. Фашисты побеждают!",

            ["end.liberals_win"] = "🟦 <b>Либералы побеждают!</b>",
            ["end.fascists_win"] = "🟥 <b>Фашисты побеждают!</b>",
            ["end.generic"] = "Игра закончена",
            ["end.reason.liberal_policies"] = "Принято 5 либеральных законов.",
            ["end.reason.fascist_policies"] = "Принято 6 фашистских законов.",
            ["end.reason.hitler_elected"] = "Гитлер избран канцлером (3+ фашистских).",
            ["end.reason.hitler_executed"] = "Гитлер казнён.",
            ["end.roles_header"] = "Роли:",

            ["err.unsupported_chat"] = "Secret Hitler доступен в группе или личных сообщениях с ботом.",
            ["err.join_missing_code"] = "Укажи код: /sh join КОД",
            ["err.not_enough_coins"] = "Не хватает монет. Бай-ин: {0}.",
            ["err.already_in_game"] = "Ты уже в игре. Сначала выйди: /sh leave.",
            ["err.game_not_found"] = "Такой игры нет или она закрыта.",
            ["err.game_full"] = "Игра заполнена (макс. 10).",
            ["err.game_in_progress"] = "Игра уже идёт — сейчас выйти нельзя.",
            ["err.not_host"] = "Только хост может начать игру.",
            ["err.not_in_game"] = "Ты сейчас не в игре.",
            ["err.not_enough_players"] = "Нужно минимум 5 игроков.",
            ["err.wrong_phase"] = "Сейчас не тот момент.",
            ["err.not_president"] = "Это действие доступно только президенту.",
            ["err.not_chancellor"] = "Это действие доступно только канцлеру.",
            ["err.invalid_target"] = "Нельзя выбрать этого игрока.",
            ["err.term_limited"] = "Этот игрок был в последнем правительстве.",
            ["err.already_voted"] = "Ты уже проголосовал.",
            ["err.invalid_policy"] = "Неверная карта.",
            ["err.generic"] = "Ошибка.",
        }),
    ];
}

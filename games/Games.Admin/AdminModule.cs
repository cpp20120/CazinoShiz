using BotFramework.Sdk;

namespace Games.Admin;

public sealed class AdminModule : IModule
{
    public string Id => "admin";
    public string DisplayName => "🛠 Admin";
    public string Version => "1.0.0";

    public void ConfigureServices(IModuleServiceCollection services)
    {
        services
            .BindOptions<AdminOptions>(AdminOptions.SectionName)
            .AddScoped<IAdminStore, AdminStore>()
            .AddScoped<IAdminService, AdminService>()
            .AddScoped<IChatsStore, ChatsStore>()
            .AddHandler<AdminHandler>()
            .AddHandler<AnalyticsHandler>()
            .AddHandler<ChatsHandler>();
    }

    public IModuleMigrations GetMigrations() => new AdminMigrations();

    public IReadOnlyList<LocaleBundle> GetLocales() =>
    [
        new("ru", new Dictionary<string, string>
        {
            ["display_name"] = "Admin",

            ["err.not_admin"] = "У вас нет прав, доступно только владельцу бота",

            ["run.help"] = "available: \n- whoami \n- usersync \n- userinfo (replied) \n- pay <user_id> <amount> \n- getUser <user_id> \n- clearbets",

            ["usersync.done"] = "Пользователи синхронизированы",

            ["userinfo.reply_hint"] = "Ответьте на сообщение пользователя с этой командой чтобы узнать его ID",
            ["userinfo.result"] = "ID отправителя: <code>{0}</code>",

            ["pay.usage"] = "Хинт: /run pay <user_id> <amount>",
            ["pay.not_found"] = "Пользователь не найден",
            ["pay.result"] = "Баланс юзера {0} ({1}) \n{2}{3} -> {4}",

            ["getuser.usage"] = "Хинт: /run getUser <user_id>",

            ["whoami.result"] = "user_id: <code>{0}</code>\nchat_id: <code>{1}</code>\nusername: <code>{2}</code>\nfirst_name: <code>{3}</code>\nadmin: <code>{4}</code>",

            ["clearbets.empty"] = "В этом чате нет зависших мини-игровых ставок.",
            ["clearbets.done"] = "Очищено ставок: {0}. Возвращено монет: {1}.",

            ["rename.usage"] = "/rename <old_name> <new_name/* to clear>",
            ["rename.set"] = "Renamed {0} to {1}",
            ["rename.cleared"] = "Renaming for {0} cleared",
            ["rename.nochange"] = "Renaming for {0} not set",

            ["analytics.private_only"] = "🚫 <b>/analytics</b> работает только в личке с ботом.",
            ["analytics.not_admin"] = "🚫 <b>/analytics</b> доступна только администратору бота.",
            ["analytics.disabled"] = "📊 <b>Analytics</b>: ClickHouse не настроен.\n<i>{0}</i>",
            ["analytics.unreachable"] = "📊 <b>Analytics</b>: ClickHouse недоступен.\n<i>{0}</i>",
            ["analytics.query_failed"] = "📊 <b>Analytics</b>: запрос упал.\n<code>{0}</code>",
            ["analytics.header"] = "📊 <b>Analytics</b>\nproject: <code>{0}</code> · table: <code>{1}</code>\nrows total: <b>{2}</b>\ngenerated: <code>{3}</code>",
            ["analytics.window.header"] = "⏱ <b>{0}</b>",
            ["analytics.window.totals"] = "events: <b>{0}</b> · users: <b>{1}</b>",
            ["analytics.window.top_events"] = "<i>top events:</i>",
            ["analytics.window.top_modules"] = "<i>top modules:</i>",
            ["analytics.window.top_users"] = "<i>top users:</i>",
            ["analytics.timeline.header"] = "📅 <b>last {0} days</b>",

            ["chats.private_only"] = "🚫 <b>/chats</b> работает только в личке с ботом.",
            ["chats.not_admin"] = "🚫 <b>/chats</b> доступна только администратору бота.",
            ["chats.empty"] = "Бот ещё ни в одном чате не засветился.",
            ["chats.header.all"] = "💬 <b>Чаты бота</b> · всего <b>{0}</b>",
            ["chats.header.group"] = "💬 <b>Чаты бота</b> · группы/супергруппы · <b>{0}</b>",
            ["chats.header.private"] = "💬 <b>Чаты бота</b> · личные · <b>{0}</b>",
            ["chats.header.channel"] = "💬 <b>Чаты бота</b> · каналы · <b>{0}</b>",
            ["chats.section_header"] = "<b>{0}</b> · {1}",
            ["chats.private_label"] = "ЛС {0}",
            ["chats.unknown_label"] = "Чат {0}",
            ["chats.truncated"] = "<i>показано {0} из {1}. Используй <code>/chats full</code> для полного списка.</i>",
            ["chats.ago.now"] = "только что",
            ["chats.ago.minutes"] = "{0} мин назад",
            ["chats.ago.hours"] = "{0} ч назад",
            ["chats.ago.days"] = "{0} д назад",
            ["chats.ago.months"] = "{0} мес назад",
        }),
    ];
}

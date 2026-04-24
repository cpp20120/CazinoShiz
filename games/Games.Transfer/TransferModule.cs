using BotFramework.Sdk;

namespace Games.Transfer;

public sealed class TransferModule : IModule
{
    public string Id => "transfer";
    public string DisplayName => "💸 Перевод";
    public string Version => "1.0.0";

    public void ConfigureServices(IModuleServiceCollection services)
    {
        services
            .BindOptions<TransferOptions>(TransferOptions.SectionName)
            .AddScoped<ITransferService, TransferService>()
            .AddHandler<TransferHandler>();
    }

    public IModuleMigrations GetMigrations() => new TransferMigrations();

    public IReadOnlyList<BotCommand> GetBotCommands() =>
    [
        new BotCommand("/transfer", "transfer.cmd.transfer"),
    ];

    public IReadOnlyList<LocaleBundle> GetLocales() =>
    [
        new LocaleBundle("ru", new Dictionary<string, string>
        {
            ["display_name"] = "Перевод",
            ["cmd.transfer"] = "Перевести монеты игроку",

            ["usage"] =
                "💸 <b>Перевод</b> (комиссия 3%, округление до 0.5, затем до целых монет; мин. 1)\n\n"
                + "• Ответь реплаем на сообщение: <code>/transfer 100</code>\n"
                + "• Или: <code>/transfer @username 100</code>\n"
                + "• Или: <code>/transfer 123456789 100</code>\n\n"
                + "<i>Списывается: сумма получателю + комиссия. Работает только в группах.</i>",

            ["err.groups_only"] = "Переводы доступны только в группах и супергруппах.",
            ["err.no_target"] = "Кому перевести? Ответь реплаем, укажи @username или numeric user id.",
            ["err.self"] = "Нельзя перевести самому себе.",
            ["err.min_net"] = "Минимальная сумма получателю: <b>{0}</b> монет.",
            ["err.max_net"] = "Максимальная сумма получателю: <b>{0}</b> монет.",
            ["err.balance"] = "Не хватает монет: нужно списать <b>{0}</b> (с комиссией), у тебя <b>{1}</b>.",
            ["err.generic"] = "Перевод не выполнен. Попробуй ещё раз или проверь баланс.",

            ["ok"] = "✅ Перевёл <b>{0}</b> монет → {5}\n"
                + "Комиссия: <b>{1}</b>, списано с тебя: <b>{2}</b>\n"
                + "Твой баланс: <b>{3}</b>\n"
                + "Баланс получателя ({5}): <b>{4}</b>",
        }),
    ];
}

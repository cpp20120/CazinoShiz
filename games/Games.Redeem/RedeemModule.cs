using BotFramework.Sdk;

namespace Games.Redeem;

public sealed class RedeemModule : IModule
{
    public string Id => "redeem";
    public string DisplayName => "🎟 Redeem";
    public string Version => "1.0.0";

    public void ConfigureServices(IModuleServiceCollection services)
    {
        services
            .BindOptions<RedeemOptions>(RedeemOptions.SectionName)
            .AddSingleton<RedeemCaptchaTimeouts>()
            .AddScoped<IRedeemService, RedeemService>()
            .AddScoped<IRedeemStore, RedeemStore>()
            .AddDomainEventSubscription<RedeemDropSubscriber>("telegram_dice.redeem_code_drop_requested")
            .AddHandler<RedeemHandler>();
    }

    public IModuleMigrations GetMigrations() => new RedeemMigrations();

    public IReadOnlyList<BotCommand> GetBotCommands() =>
    [
        new BotCommand("/redeem", "redeem.cmd.redeem"),
    ];

    public IReadOnlyList<LocaleBundle> GetLocales() =>
    [
        new LocaleBundle("ru", new Dictionary<string, string>
        {
            ["display_name"] = "Redeem",
            ["cmd.redeem"] = "Активировать код",

            ["captcha.prompt"] = "<b>{0}</b>\nВыбери снизу самый подходящий смайлик",
            ["captcha.wrong"] = "Увы, неверно. Сделайте /redeem снова",
            ["captcha.timeout"] = "Вы отвечали слишком долго, используйте /redeem снова",

            ["redeem.success"] = "🎉 Код активирован! Тебе доступен ещё один спин 🎰",
            ["drop.message"] = "🎟 Выпал фриспин!\nСкопируй это в личку боту:\n<code>/redeem {0}</code>",

            ["err.only_private"] = "Активировать код можно только в личных сообщениях 😄",
            ["err.invalid_code"] = "Код недействителен",
            ["err.already_redeemed"] = "Сорри, этот код уже кто-то успел активировать 🤯",
            ["err.self_redeem"] = "Упс, а вот свой код обналичить нельзя 🥲",
            ["err.no_user"] = "Похоже, ты ещё не зарегистрирован. Сделай /start",
            ["err.lost_race"] = "Ойй 😬, кажется пока кто-то решал капчу — кодик уже уплыл! 😜",
        }),
    ];
}

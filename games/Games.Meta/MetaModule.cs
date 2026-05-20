using BotFramework.Sdk;

namespace Games.Meta;

public sealed class MetaModule : IModule
{
    public string Id => "meta";
    public string DisplayName => "⭐ Мета";
    public string Version => "0.1.0";

    public void ConfigureServices(IModuleServiceCollection services)
    {
        services
            .AddScoped<IMetaStore, MetaStore>()
            .AddScoped<IMetaService, MetaService>()
            .AddHandler<MetaHandler>();
    }

    public IModuleMigrations? GetMigrations() => new MetaMigrations();

    public IReadOnlyList<BotCommand> GetBotCommands() =>
    [
        new BotCommand("/season", "meta.cmd.season"),
        new BotCommand("/profile", "meta.cmd.profile"),
        new BotCommand("/rank", "meta.cmd.rank"),
        new BotCommand("/topseason", "meta.cmd.topseason"),
    ];

    public IReadOnlyList<LocaleBundle> GetLocales() =>
    [
        new LocaleBundle("ru", new Dictionary<string, string>
        {
            ["display_name"] = "Мета",
            ["cmd.season"] = "Текущий сезон",
            ["cmd.profile"] = "Профиль сезона",
            ["cmd.rank"] = "Ранг сезона",
            ["cmd.topseason"] = "Сезонный топ",
        }),
    ];
}

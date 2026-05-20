using BotFramework.Sdk;

namespace Games.Meta;

public sealed class MetaModule : IModule
{
    public string Id => "meta";
    public string DisplayName => "⭐ Мета";
    public string Version => "0.7.0";

    public void ConfigureServices(IModuleServiceCollection services)
    {
        services
            .AddScoped<IMetaStore, MetaStore>()
            .AddScoped<IMetaService, MetaService>()
            .AddScoped<IQuestStore, QuestStore>()
            .AddScoped<IQuestService, QuestService>()
            .AddScoped<IClanStore, ClanStore>()
            .AddScoped<IClanService, ClanService>()
            .AddScoped<ITournamentStore, TournamentStore>()
            .AddScoped<ITournamentService, TournamentService>()
            .AddScoped<IRiskStore, RiskStore>()
            .AddScoped<IRiskService, RiskService>()
            .AddDomainEventSubscription<MetaXpProjection>("meta.game_completed")
            .AddDomainEventSubscription<QuestProjection>("meta.game_completed")
            .AddDomainEventSubscription<ClanProjection>("meta.game_completed")
            .AddHandler<MetaHandler>()
            .AddHandler<TournamentHandler>()
            .AddHandler<RiskHandler>();
    }

    public IModuleMigrations? GetMigrations() => new MetaMigrations();

    public IReadOnlyList<BotCommand> GetBotCommands() =>
    [
        new BotCommand("/season", "meta.cmd.season"),
        new BotCommand("/profile", "meta.cmd.profile"),
        new BotCommand("/rank", "meta.cmd.rank"),
        new BotCommand("/topseason", "meta.cmd.topseason"),
        new BotCommand("/achievements", "meta.cmd.achievements"),
        new BotCommand("/quests", "meta.cmd.quests"),
        new BotCommand("/quest", "meta.cmd.quest"),
        new BotCommand("/clan", "meta.cmd.clan"),
        new BotCommand("/tournament", "meta.cmd.tournament"),
        new BotCommand("/tour", "meta.cmd.tour"),
        new BotCommand("/risk", "meta.cmd.risk"),
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
            ["cmd.achievements"] = "Ачивки сезона",
            ["cmd.quests"] = "Квесты сезона",
            ["cmd.quest"] = "Забрать награду за квест",
            ["cmd.clan"] = "Кланы сезона",
            ["cmd.tournament"] = "Турниры сезона",
            ["cmd.tour"] = "Турниры сезона",
            ["cmd.risk"] = "Risk control",
        }),
    ];
}

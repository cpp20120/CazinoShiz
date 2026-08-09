namespace Games.Meta.Infrastructure.Modules;

using Games.Meta.Infrastructure.Configuration;

public sealed class MetaModule : IModule
{
    public string Id => "meta";
    public string DisplayName => "⭐ Мета";
    public string Version => "0.10.0";

    public void ConfigureServices(IModuleServiceCollection services)
    {
        services
            .BindOptions<MetaOptions, MetaOptionsValidator>(MetaOptions.SectionName)
            .AddScoped<IMetaHistoryStore, MetaHistoryStore>()
            .AddScoped<IMetaReconstructionStore, MetaReconstructionStore>()
            .AddScoped<IMetaStore, MetaStore>()
            .AddSingleton<IMetaReadModel, Games.Meta.Infrastructure.ReadModels.RedisMetaReadModel>()
            .AddScoped<IMetaService, MetaService>()
            .AddScoped<ISeasonRewardService, SeasonRewardService>()
            .AddSingleton<IQuestCatalog, JsonQuestCatalog>()
            .AddScoped<IQuestStore, QuestStore>()
            .AddScoped<IQuestService, QuestService>()
            .AddScoped<IClanStore, ClanStore>()
            .AddScoped<IClanService, ClanService>()
            .AddScoped<ITournamentStore, TournamentStore>()
            .AddScoped<TournamentCommandExecutor>()
            .AddScoped<ITournamentWorkflow, DurableTournamentWorkflow>()
            .AddScoped<ITournamentService, TournamentService>()
            .AddScoped<IRiskStore, RiskStore>()
            .AddScoped<IRiskService, RiskService>()
            .AddAdminEffectHandler<Games.Meta.Application.Effects.MetaSeasonCreateAdminEffectHandler>()
            .AddAdminEffectHandler<Games.Meta.Application.Effects.MetaSeasonPrepareAdminEffectHandler>()
            .AddAdminEffectHandler<Games.Meta.Application.Effects.MetaSeasonActivateAdminEffectHandler>()
            .AddAdminEffectHandler<Games.Meta.Application.Effects.MetaSeasonFinishAdminEffectHandler>()
            .AddAdminEffectHandler<Games.Meta.Application.Effects.MetaSeasonConfigAdminEffectHandler>()
            .AddAdminEffectHandler<Games.Meta.Application.Effects.MetaSeasonPlayerRewardsAdminEffectHandler>()
            .AddAdminEffectHandler<Games.Meta.Application.Effects.MetaSeasonClanRewardsAdminEffectHandler>()
            .AddAdminEffectHandler<Games.Meta.Application.Effects.MetaAlertStatusAdminEffectHandler>()
            .AddAdminEffectHandler<Games.Meta.Application.Effects.MetaQuestCatalogSaveAdminEffectHandler>()
            .AddAdminEffectHandler<Games.Meta.Application.Effects.MetaQuestCatalogReloadAdminEffectHandler>()
            .AddAtomicEffectHandler<Games.Meta.Application.Effects.QuestClaimAtomicEffectHandler>()
            .AddAtomicEffectHandler<Games.Meta.Application.Effects.QuestProgressAtomicEffectHandler>()
            .AddAtomicEffectHandler<Games.Meta.Application.Effects.TournamentCreateAtomicEffectHandler>()
            .AddAtomicEffectHandler<Games.Meta.Application.Effects.TournamentJoinAtomicEffectHandler>()
            .AddAtomicEffectHandler<Games.Meta.Application.Effects.TournamentStartAtomicEffectHandler>()
            .AddAtomicEffectHandler<Games.Meta.Application.Effects.TournamentReportAtomicEffectHandler>()
            .AddAtomicEffectHandler<Games.Meta.Application.Effects.TournamentFinishAtomicEffectHandler>()
            .AddAtomicEffectHandler<Games.Meta.Application.Effects.TournamentCancelAtomicEffectHandler>()
            .AddAtomicEffectHandler<Games.Meta.Application.Effects.SeasonPlayerRewardsAtomicEffectHandler>()
            .AddAtomicEffectHandler<Games.Meta.Application.Effects.SeasonClanRewardsAtomicEffectHandler>()
            .AddDomainEventSubscription<MetaXpProjection>("meta.game_completed")
            .AddDomainEventSubscription<QuestProjection>("meta.game_completed")
            .AddDomainEventSubscription<ClanProjection>("meta.game_completed")
            .AddRecurringScheduledCommand<MetaSeasonRolloverJob>()
            .AddRecurringScheduledCommand<MetaAnalyticsSnapshotJob>()
            .AddRecurringScheduledCommand<OperationsReportingJob>();
    }

    public IModuleMigrations GetMigrations() => new MetaMigrations();

    public IReadOnlyList<BotCommand> GetBotCommands() =>
    [
        new BotCommand("/menu", "meta.cmd.menu"),
        new BotCommand("/season", "meta.cmd.season"),
        new BotCommand("/profile", "meta.cmd.profile"),
        new BotCommand("/rank", "meta.cmd.rank"),
        new BotCommand("/topseason", "meta.cmd.topseason"),
        new BotCommand("/achievements", "meta.cmd.achievements"),
        new BotCommand("/streaks", "meta.cmd.streaks"),
        new BotCommand("/quests", "meta.cmd.quests"),
        new BotCommand("/quest", "meta.cmd.quest"),
        new BotCommand("/clan", "meta.cmd.clan"),
        new BotCommand("/tournament", "meta.cmd.tournament"),
        new BotCommand("/tour", "meta.cmd.tour"),
        new BotCommand("/risk", "meta.cmd.risk"),
        new BotCommand("/mystats", "meta.cmd.mystats"),
    ];

    public IReadOnlyList<LocaleBundle> GetLocales() =>
    [
        new LocaleBundle("ru", new Dictionary<string, string>
(StringComparer.Ordinal) {
            ["display_name"] = "Мета",
            ["cmd.menu"] = "Интерактивное меню и профиль",
            ["cmd.season"] = "Текущий сезон",
            ["cmd.profile"] = "Профиль сезона",
            ["cmd.rank"] = "Ранг сезона",
            ["cmd.topseason"] = "Сезонный топ",
            ["cmd.achievements"] = "Ачивки сезона",
            ["cmd.streaks"] = "Стрики по играм",
            ["cmd.quests"] = "Квесты сезона",
            ["cmd.quest"] = "Забрать награду за квест",
            ["cmd.clan"] = "Кланы сезона",
            ["cmd.tournament"] = "Турниры сезона",
            ["cmd.tour"] = "Турниры сезона",
            ["cmd.risk"] = "Risk control",
            ["cmd.mystats"] = "Моя статистика и лимиты",
        }),
    ];
}

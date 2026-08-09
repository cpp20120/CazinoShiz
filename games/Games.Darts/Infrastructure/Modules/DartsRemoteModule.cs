namespace Games.Darts.Infrastructure.Modules;

using BotFramework.Host.Execution;
using BotFramework.Host.Wagering;
using BotFramework.Sdk.Execution;
using Games.Darts.Application.Execution;
using Games.Darts.Application.Wagering;
using Games.Darts.Infrastructure.Configuration;

/// <summary>Backend composition without Telegram roll-delivery worker.</summary>
public sealed class DartsRemoteModule : IModule
{
    public string Id => "darts";
    public string DisplayName => "Darts backend";
    public string Version => "1.0.0";

    public void ConfigureServices(IModuleServiceCollection services)
    {
        services
            .BindOptions<DartsOptions, DartsOptionsValidator>(DartsOptions.SectionName)
            .AddSingleton<IDartsRollQueue, ClientDeliveredDartsRollQueue>()
            .AddScoped<IDartsRoundStore, DartsRoundStore>()
            .AddScoped<IDartsService, DartsService>()
            .AddScoped<IGameAction<DartsPlaceBetCommand, DartsQueuedState, DartsBetResult>, DartsPlaceBetAction>()
            .AddScoped<GameExecutionDescriptor<DartsPlaceBetCommand, DartsQueuedState, DartsBetResult>, DartsPlaceBetDescriptor>()
            .AddScoped<IGameStateStore<DartsPlaceBetCommand, DartsQueuedState>, DartsPlaceBetStateStore>()
            .AddScoped<IGameAction<DartsResolveRoundCommand, DartsQueuedState, DartsThrowResult>, DartsResolveRoundAction>()
            .AddScoped<GameExecutionDescriptor<DartsResolveRoundCommand, DartsQueuedState, DartsThrowResult>, DartsResolveRoundDescriptor>()
            .AddScoped<IGameStateStore<DartsResolveRoundCommand, DartsQueuedState>, DartsResolveRoundStateStore>()
            .AddScoped<IGameAction<DartsAbortRoundCommand, DartsQueuedState, DartsAbortRoundResult>, DartsAbortRoundAction>()
            .AddScoped<GameExecutionDescriptor<DartsAbortRoundCommand, DartsQueuedState, DartsAbortRoundResult>, DartsAbortRoundDescriptor>()
            .AddScoped<IGameStateStore<DartsAbortRoundCommand, DartsQueuedState>, DartsAbortRoundStateStore>()
            .AddScoped<IGameAction<DartsQuickThrowCommand, NoGameState, DartsThrowResult>, DartsQuickThrowAction>()
            .AddScoped<GameExecutionDescriptor<DartsQuickThrowCommand, NoGameState, DartsThrowResult>, DartsQuickThrowDescriptor>()
            .AddScoped<IGameStateStore<DartsQuickThrowCommand, NoGameState>, DartsNoGameStateStore>()
            .AddScoped<IWagerGameResolver, DartsWagerResolver>()
            .AddScoped<IWagerPayoutPolicy, DartsWagerPayoutPolicy>();
    }

    public IModuleMigrations GetMigrations() => new DartsMigrations();
    public IReadOnlyList<BotCommand> GetBotCommands() => [];
    public IReadOnlyList<LocaleBundle> GetLocales() => [];
}

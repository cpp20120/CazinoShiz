
namespace Games.Blackjack.Infrastructure.Modules;

using BotFramework.Contracts.Messaging;
using BotFramework.Contracts.Wagering;
using BotFramework.Host.Execution;
using BotFramework.Scheduling.Abstractions;
using BotFramework.Sdk.Execution;
using Games.Blackjack.Application.Execution;
using Games.Blackjack.Application.Wagering;
using Games.Blackjack.Contracts.Domain.Results;
using Games.Blackjack.Contracts.Integration;
using Games.Blackjack.Infrastructure.Configuration;

public sealed class BlackjackModule : IModule
{
    public string Id => "blackjack";
    public string DisplayName => "🃏 Блэкджек";
    public string Version => "1.0.0";

    public void ConfigureServices(IModuleServiceCollection services)
    {
        services
            .BindOptions<BlackjackOptions, BlackjackOptionsValidator>(BlackjackOptions.SectionName)
            .AddScoped<IBlackjackService, BlackjackService>()
            .AddScoped<IBlackjackClient, LocalBlackjackClient>()
            .AddScoped<IBlackjackStateReader, BlackjackStateReader>()
            .AddScoped<IGameAction<BlackjackStartCommand, BlackjackGameState, BlackjackResult>, BlackjackStartAction>()
            .AddScoped<GameExecutionDescriptor<BlackjackStartCommand, BlackjackGameState, BlackjackResult>, BlackjackStartDescriptor>()
            .AddScoped<IGameStateStore<BlackjackStartCommand, BlackjackGameState>, PostgresJsonGameStateStore<BlackjackStartCommand, BlackjackGameState, BlackjackResult>>()
            .AddScoped<IGameAction<BlackjackTurnCommand, BlackjackGameState, BlackjackResult>, BlackjackTurnAction>()
            .AddScoped<GameExecutionDescriptor<BlackjackTurnCommand, BlackjackGameState, BlackjackResult>, BlackjackTurnDescriptor>()
            .AddScoped<IGameStateStore<BlackjackTurnCommand, BlackjackGameState>, PostgresJsonGameStateStore<BlackjackTurnCommand, BlackjackGameState, BlackjackResult>>()
            .AddScoped<IGameAction<BlackjackTimeoutCommand, BlackjackGameState, BlackjackResult>, BlackjackTimeoutAction>()
            .AddScoped<GameExecutionDescriptor<BlackjackTimeoutCommand, BlackjackGameState, BlackjackResult>, BlackjackTimeoutDescriptor>()
            .AddScoped<IGameStateStore<BlackjackTimeoutCommand, BlackjackGameState>, PostgresJsonGameStateStore<BlackjackTimeoutCommand, BlackjackGameState, BlackjackResult>>()
            .AddScoped<IScheduledCommand, AtomicGameScheduledCommand<BlackjackTimeoutCommand, BlackjackGameState, BlackjackResult>>()
            .AddScoped<IGameAction<BlackjackSetMessageCommand, BlackjackGameState, BlackjackResult>, BlackjackSetMessageAction>()
            .AddScoped<GameExecutionDescriptor<BlackjackSetMessageCommand, BlackjackGameState, BlackjackResult>, BlackjackSetMessageDescriptor>()
            .AddScoped<IGameStateStore<BlackjackSetMessageCommand, BlackjackGameState>, PostgresJsonGameStateStore<BlackjackSetMessageCommand, BlackjackGameState, BlackjackResult>>();

        services
            .AddScoped<IGameAction<BlackjackWagerStart, BlackjackWagerState, BlackjackWagerResult>, BlackjackWagerStartAction>()
            .AddScoped<GameExecutionDescriptor<BlackjackWagerStart, BlackjackWagerState, BlackjackWagerResult>, BlackjackWagerStartDescriptor>()
            .AddScoped<IGameStateStore<BlackjackWagerStart, BlackjackWagerState>, PostgresJsonGameStateStore<BlackjackWagerStart, BlackjackWagerState, BlackjackWagerResult>>()
            .AddScoped<IGameAction<BlackjackWagerHit, BlackjackWagerState, BlackjackWagerResult>, BlackjackWagerHitAction>()
            .AddScoped<GameExecutionDescriptor<BlackjackWagerHit, BlackjackWagerState, BlackjackWagerResult>, BlackjackWagerHitDescriptor>()
            .AddScoped<IGameStateStore<BlackjackWagerHit, BlackjackWagerState>, PostgresJsonGameStateStore<BlackjackWagerHit, BlackjackWagerState, BlackjackWagerResult>>()
            .AddScoped<IGameAction<BlackjackWagerStand, BlackjackWagerState, BlackjackWagerResult>, BlackjackWagerStandAction>()
            .AddScoped<GameExecutionDescriptor<BlackjackWagerStand, BlackjackWagerState, BlackjackWagerResult>, BlackjackWagerStandDescriptor>()
            .AddScoped<IGameStateStore<BlackjackWagerStand, BlackjackWagerState>, PostgresJsonGameStateStore<BlackjackWagerStand, BlackjackWagerState, BlackjackWagerResult>>()
            .AddScoped<IGameAction<BlackjackWagerTimeout, BlackjackWagerState, BlackjackWagerResult>, BlackjackWagerTimeoutAction>()
            .AddScoped<GameExecutionDescriptor<BlackjackWagerTimeout, BlackjackWagerState, BlackjackWagerResult>, BlackjackWagerTimeoutDescriptor>()
            .AddScoped<IGameStateStore<BlackjackWagerTimeout, BlackjackWagerState>, PostgresJsonGameStateStore<BlackjackWagerTimeout, BlackjackWagerState, BlackjackWagerResult>>()
            .AddScoped<IScheduledCommand, GameStateScheduledCommand<BlackjackWagerTimeout, BlackjackWagerState, BlackjackWagerResult>>()
            .AddScoped<BlackjackWagerCommandHandler>()
            .AddScoped<IIntegrationCommandHandler<BlackjackWagerStart>, BlackjackWagerCommandHandler>()
            .AddScoped<IIntegrationCommandHandler<BlackjackWagerHit>, BlackjackWagerCommandHandler>()
            .AddScoped<IIntegrationCommandHandler<BlackjackWagerStand>, BlackjackWagerCommandHandler>()
            .AddScoped<IIntegrationCommandHandler<BlackjackWagerTimeout>, BlackjackWagerCommandHandler>()
            .AddScoped<IWagerGameCommandFactory, BlackjackWagerGameCommandFactory>()
            .AddScoped<IWagerSettlementCommandFactory, BlackjackWagerSettlementFactory>()
            .AddDomainEventSubscription<BlackjackWagerOutcomeIntegrationBridge>(
                "blackjack.wager.outcome_declared");
    }

    public IModuleMigrations GetMigrations() => new BlackjackMigrations();

    public IReadOnlyList<BotCommand> GetBotCommands() =>
    [
        new BotCommand("/blackjack", "blackjack.cmd.blackjack"),
    ];

    public IReadOnlyList<LocaleBundle> GetLocales() =>
    [
        new LocaleBundle("ru", new Dictionary<string, string>
(StringComparer.Ordinal) {
            ["display_name"] = "Блэкджек",
            ["cmd.blackjack"] = "Сыграть в блэкджек",
            ["usage"] = "🃏 <b>Блэкджек</b>\n\n/blackjack <i>ставка</i> — начать раздачу (от {0} до {1}).\nКнопки: Ещё / Стоп / Удвоить.",
            ["render.header"] = "🃏 <b>Блэкджек</b> — ставка: {0}",
            ["render.dealer"] = "Дилер: {0}{1}",
            ["render.player"] = "Ты: {0} ({1})",
            ["render.balance"] = "Баланс: {0}",
            ["btn.hit"] = "🃏 Ещё",
            ["btn.stand"] = "✋ Стоп",
            ["btn.double"] = "💰 Удвоить",
            ["outcome.player_blackjack"] = "🎉 Блэкджек! Выигрыш: +{0}",
            ["outcome.player_win"] = "✅ Победа! Выигрыш: +{0}",
            ["outcome.dealer_bust"] = "💥 Дилер перебрал! Выигрыш: +{0}",
            ["outcome.player_bust"] = "💀 Перебор. Потеря: -{1}",
            ["outcome.dealer_win"] = "😞 Дилер победил. Потеря: -{1}",
            ["outcome.push"] = "🤝 Ничья. Ставка возвращена.",
            ["err.invalid_bet"] = "Ставка должна быть от {0} до {1} монет.",
            ["err.not_enough_coins"] = "Не хватает монет для этой ставки.",
            ["err.hand_in_progress"] = "Раздача уже идёт — сначала доиграй её.",
            ["err.no_active_hand"] = "Нет активной раздачи. /blackjack <i>ставка</i>",
            ["err.cannot_double"] = "Удвоить можно только на первом действии.",
            ["err.generic"] = "Ошибка.",
        }),
    ];
}

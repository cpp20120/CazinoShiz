using BotFramework.Host.Contracts.Telegram;
using BotFramework.Host.TelegramOutbox;
using BotFramework.Host.Contracts.Discord;
using BotFramework.Host.DiscordOutbox;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using BotFramework.Contracts.Messaging;
using BotFramework.Contracts.Caching;
using BotFramework.Host.Caching;
using BotFramework.Host.Messaging;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection.Extensions;
using BotFramework.Scheduling.Quartz;
using BotFramework.Contracts.Games;
using BotFramework.Contracts.Operations;
using BotFramework.Host.Events.Replay;
using BotFramework.Host.Fairness;
using BotFramework.Host.Games;
using BotFramework.Host.Execution;
using BotFramework.Host.Execution.Lifecycle;
using BotFramework.Host.Execution.Telegram;
using BotFramework.Rendering;
using BotFramework.Host.Configuration.Validation;
using BotFramework.Host.Admin.Execution;
using BotFramework.Host.Admin.Effects;
using BotFramework.Host.Composition.ServiceDatabases;
using BotFramework.Host.RateLimiting;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using BotFramework.Contracts.RateLimiting;
using BotFramework.Contracts.Tenancy;
using BotFramework.Contracts.Economics;
using BotFramework.Contracts.Cases;
using BotFramework.Contracts.Ledger;
using BotFramework.Contracts.Wagering;
using BotFramework.Host.Tenancy;
using BotFramework.Host.Economics;
using BotFramework.Host.Cases;
using BotFramework.Host.Ledger;
using BotFramework.Host.Wagering;
using BotFramework.Sdk.Execution;
using BotFramework.Sdk.Execution.Lifecycle;

namespace BotFramework.Host.Composition.Builder;

public static class BotFrameworkBuilderExtensions
{
    public static IBotFrameworkBuilder AddBackendFramework(this IHostApplicationBuilder builder)
    {
        var services = builder.Services;
        var configuration = builder.Configuration;
        var walletRemote = string.Equals(
            configuration["Services:Wallet:Mode"],
            "Grpc",
            StringComparison.OrdinalIgnoreCase);

        DapperTypeHandlers.Register();

        services.TryAddSingleton(TimeProvider.System);
        services.AddBotFrameworkRendering(configuration);

        services.AddRazorPages();
        services.AddMemoryCache();
        services.AddDistributedMemoryCache();
        services.AddAntiforgery();
        services.AddSession(opts =>
        {
            opts.Cookie.HttpOnly = true;
            opts.Cookie.SameSite = SameSiteMode.Lax;
            opts.Cookie.IsEssential = true;
            opts.IdleTimeout = TimeSpan.FromDays(30);
        });
        services.AddSingleton<TelegramLoginVerifier>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<BotFrameworkOptions>>().Value;
            return new TelegramLoginVerifier(opts.Token);
        });
        services.AddScoped<IAdminAuditLog, AdminAuditLog>();
        services.AddScoped<IAdminAuditReader, AdminAuditReader>();

        services.AddOptions<BotFrameworkOptions>()
            .Bind(configuration.GetSection(BotFrameworkOptions.SectionName))
            .Validate(options => !options.Enabled || !string.IsNullOrWhiteSpace(options.Token),
                "Bot:Token is required when Bot:Enabled is true.")
            .Validate(options => !options.IsProduction || Uri.TryCreate(options.WebhookBaseUrl, UriKind.Absolute, out var uri)
                && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase),
                "Bot:WebhookBaseUrl must be an absolute HTTPS URL in production.")
            .ValidateOnStart();

        services.AddOptions<PostgresConnectionOptions>()
            .Configure(options => options.ConnectionString = configuration.GetConnectionString("Postgres") ?? "")
            .Validate(options => !string.IsNullOrWhiteSpace(options.ConnectionString),
                "ConnectionStrings:Postgres is required.")
            .ValidateOnStart();
        services.AddOptions<ServiceOwnershipOptions>()
            .Bind(configuration.GetSection(ServiceOwnershipOptions.SectionName));
        services.AddSingleton<IServiceOwnershipValidator, PostgresServiceOwnershipValidator>();
        services.AddHostedService<ServiceOwnershipHostedService>();
        var operationsSection = configuration.GetSection(OperationsSecurityOptions.SectionName);
        services.AddOptions<OperationsSecurityOptions>()
            .Bind(operationsSection)
            .Configure(options => options.Required = operationsSection.Exists())
            .Validate(options => !options.Required || !string.IsNullOrWhiteSpace(options.ApiKey),
                "Services:Operations:ApiKey is required when the Operations section is configured.")
            .ValidateOnStart();

        services.AddScoped<ICommandBus, CommandBus>();
        services.AddOptions<RateLimitOptions>()
            .Bind(configuration.GetSection(RateLimitOptions.SectionName))
            .Configure(options => options.RedisConnectionString ??= configuration["Redis:ConnectionString"])
            .Validate(options => options.LocalMaxKeys > 0, "RateLimit:LocalMaxKeys must be positive.")
            .ValidateOnStart();
        services.AddSingleton<IRateLimiter, RedisRateLimiter>();
        services.AddSingleton<PostgresRateLimitPolicyProvider>();
        services.AddSingleton<IRateLimitPolicyProvider>(sp => sp.GetRequiredService<PostgresRateLimitPolicyProvider>());
        services.AddSingleton<IRateLimitPolicyAdmin>(sp => sp.GetRequiredService<PostgresRateLimitPolicyProvider>());
        services.AddScoped<ITenantContextAccessor, TenantContextAccessor>();
        services.AddScoped<RateLimitRequestState>();
        services.AddScoped<ICommandMiddleware, DistributedRateLimitMiddleware>();
        services.AddMediatR(configuration =>
            configuration.RegisterServicesFromAssemblyContaining<LocalRequestClient>());
        services.AddScoped<IRequestClient, LocalRequestClient>();

        services.AddOptions<RedisOptions>()
            .Bind(configuration.GetSection(RedisOptions.SectionName))
            .Validate(options => !options.Enabled || !string.IsNullOrWhiteSpace(options.ConnectionString),
                "Redis:ConnectionString is required when Redis:Enabled is true.")
            .Validate(options => options.PartitionCount > 0 && options.MaxProcessingAttempts > 0,
                "Redis partition and retry counts must be positive.")
            .ValidateOnStart();
        services.AddOptions<TelegramOutboxTransportOptions>()
            .Bind(configuration.GetSection(TelegramOutboxTransportOptions.SectionName))
            .Validate(options => string.Equals(options.Transport, "Local", StringComparison.OrdinalIgnoreCase)
                || string.Equals(options.Transport, "Cap", StringComparison.OrdinalIgnoreCase),
                "TelegramOutbox:Transport must be Local or Cap.")
            .ValidateOnStart();
        var redisEnabled = configuration.GetValue<bool>($"{RedisOptions.SectionName}:Enabled");
        var redisConn = configuration.GetValue<string>($"{RedisOptions.SectionName}:ConnectionString");
        var useCapOutboxTransport = string.Equals(
            configuration[$"{TelegramOutboxTransportOptions.SectionName}:Transport"],
            "Cap",
            StringComparison.OrdinalIgnoreCase);
        if (redisEnabled && string.IsNullOrWhiteSpace(redisConn))
        {
            throw new InvalidOperationException(
                "Redis:Enabled is true but Redis:ConnectionString is not set.");
        }

        if (useCapOutboxTransport && !redisEnabled)
        {
            throw new InvalidOperationException(
                "TelegramOutbox:Transport=Cap requires Redis because CAP transport is enabled.");
        }

        var botIsProduction = configuration.GetValue<bool>($"{BotFrameworkOptions.SectionName}:IsProduction");
        if (botIsProduction && !redisEnabled)
        {
            throw new InvalidOperationException(
                "Production mode requires Redis. Set Redis:Enabled=true and Redis:ConnectionString.");
        }

        var pgConnStr = configuration.GetConnectionString("Postgres")!;
        var configuredBackendServiceName = configuration["Backend:ServiceName"]
            ?? configuration["Service:Name"];
        var backendServiceName = configuredBackendServiceName ?? "backend";
        builder.AddFrameworkIntegrationMessaging(backendServiceName);
        var capTransport = FrameworkCapTransport.Resolve(configuration);
        // Keep the existing monolith Quartz partition so an upgrade does not
        // orphan schedules created before the distributed profile existed.
        var schedulerName = configuredBackendServiceName ?? "CasinoShiz";
        services.AddSingleton<DomainEventSubscriptionDispatcher>();
        if (capTransport != FrameworkCapTransportKind.Local)
        {
            services.AddSingleton<CapEventBus>();
            services.AddSingleton<IDomainEventBus>(sp => sp.GetRequiredService<CapEventBus>());
            services.AddSingleton<CapEventConsumer>();
        }
        else
        {
            services.AddSingleton<IDomainEventBus>(sp => new InProcessEventBus(
                sp.GetRequiredService<DomainEventSubscriptionDispatcher>()));
        }
        services.AddHostedService<EventSubscriptionInitializer>();

        services.AddSingleton<HealthEndpoint>();
        services.AddSingleton<ILocalizer, Localizer>();
        services.AddSingleton<INpgsqlConnectionFactory, NpgsqlConnectionFactory>();
        services.AddSingleton<ITenantContextProvisioner, PostgresTenantContextProvisioner>();
        services.AddScoped<BotFramework.Contracts.Wagering.IWagerOperationStore, BotFramework.Host.Wagering.PostgresWagerOperationStore>();
        services.AddScoped<BotFramework.Contracts.Wagering.IMultiPartyWagerCoordinator, BotFramework.Host.Wagering.MultiPartyWagerCoordinator>();
        services.AddScoped<BotFramework.Host.Wagering.PostgresMultiPartyWagerStore>();
        services.AddScoped<BotFramework.Host.Wagering.MultiPartyWagerWorkflowExecutor>();
        services.AddScoped<BotFramework.Host.Wagering.WageringCoordinator>();
        services.AddScoped<BotFramework.Contracts.Messaging.IIntegrationCommandHandler<BotFramework.Contracts.Wagering.WagerRequested>>(sp => sp.GetRequiredService<BotFramework.Host.Wagering.WageringCoordinator>());
        services.AddScoped<BotFramework.Contracts.Messaging.IIntegrationEventHandler<BotFramework.Contracts.Ledger.LedgerOperationCompleted>>(sp => sp.GetRequiredService<BotFramework.Host.Wagering.WageringCoordinator>());
        services.AddScoped<BotFramework.Contracts.Messaging.IIntegrationEventHandler<BotFramework.Contracts.Wagering.GameOutcomeDeclared>>(sp => sp.GetRequiredService<BotFramework.Host.Wagering.WageringCoordinator>());
        services.AddScoped<BotFramework.Host.Wagering.PostgresWagerLedgerCommandHandler>();
        services.AddScoped<BotFramework.Contracts.Messaging.IIntegrationCommandHandler<BotFramework.Contracts.Wagering.LedgerReservationRequested>>(sp => sp.GetRequiredService<BotFramework.Host.Wagering.PostgresWagerLedgerCommandHandler>());
        services.AddScoped<BotFramework.Contracts.Messaging.IIntegrationCommandHandler<BotFramework.Contracts.Wagering.LedgerSettlementRequested>>(sp => sp.GetRequiredService<BotFramework.Host.Wagering.PostgresWagerLedgerCommandHandler>());
        services.AddScoped<BotFramework.Contracts.Messaging.IIntegrationCommandHandler<BotFramework.Contracts.Wagering.LedgerReservationRefundRequested>>(sp => sp.GetRequiredService<BotFramework.Host.Wagering.PostgresWagerLedgerCommandHandler>());
        if (!walletRemote)
        {
            services.AddScoped<PostgresLedgerOperationCommandHandler>();
            services.AddScoped<IIntegrationCommandHandler<LedgerHoldRequested>>(sp => sp.GetRequiredService<PostgresLedgerOperationCommandHandler>());
            services.AddScoped<IIntegrationCommandHandler<LedgerCaptureRequested>>(sp => sp.GetRequiredService<PostgresLedgerOperationCommandHandler>());
            services.AddScoped<IIntegrationCommandHandler<LedgerReleaseRequested>>(sp => sp.GetRequiredService<PostgresLedgerOperationCommandHandler>());
            services.AddScoped<IIntegrationCommandHandler<LedgerRefundRequested>>(sp => sp.GetRequiredService<PostgresLedgerOperationCommandHandler>());
            services.AddScoped<IIntegrationCommandHandler<LedgerTransferRequested>>(sp => sp.GetRequiredService<PostgresLedgerOperationCommandHandler>());
            services.AddScoped<IIntegrationCommandHandler<LedgerAdjustmentRequested>>(sp => sp.GetRequiredService<PostgresLedgerOperationCommandHandler>());
        }
        services.AddScoped<PostgresCaseCommandHandler>();
        services.AddScoped<IIntegrationCommandHandler<CaseOpenRequested>>(sp => sp.GetRequiredService<PostgresCaseCommandHandler>());
        services.AddScoped<IIntegrationCommandHandler<CaseEvidenceRequested>>(sp => sp.GetRequiredService<PostgresCaseCommandHandler>());
        services.AddScoped<IIntegrationCommandHandler<CaseReviewRequested>>(sp => sp.GetRequiredService<PostgresCaseCommandHandler>());
        services.AddScoped<IIntegrationCommandHandler<CaseResolveRequested>>(sp => sp.GetRequiredService<PostgresCaseCommandHandler>());
        services.AddScoped<IIntegrationCommandHandler<CaseAppealRequested>>(sp => sp.GetRequiredService<PostgresCaseCommandHandler>());
        services.AddScoped<BotFramework.Host.Wagering.RedisWagerOperationProjection>();
        services.AddSingleton<BotFramework.Contracts.Wagering.IWagerOperationResultCache, BotFramework.Host.Wagering.RedisWagerOperationResultCache>();
        services.AddScoped<BotFramework.Contracts.Messaging.IIntegrationEventHandler<BotFramework.Contracts.Wagering.WagerSettled>>(sp => sp.GetRequiredService<BotFramework.Host.Wagering.RedisWagerOperationProjection>());
        services.AddHealthChecks()
            .AddCheck<PostgresDatabaseHealthCheck>(
                "postgres",
                failureStatus: Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy,
                tags: ["ready"]);
        services.AddSingleton<IGameExecutionSessionFactory, PostgresGameExecutionSessionFactory>();
        services.AddSingleton<IGameRuntimeCapabilityProvider, CoreGameRuntimeCapabilityProvider>();
        services.AddSingleton<IGameCapabilityValidator, GameRuntimeCapabilityValidator>();
        services.AddSingleton<IGameSessionStore, PostgresGameSessionStore>();
        services.AddScoped<IGameSessionService, DefaultGameSessionService>();
        services.AddSingleton<PostgresGameInputRequestService>();
        services.AddSingleton<IGameInputRequestService>(sp => sp.GetRequiredService<PostgresGameInputRequestService>());
        services.AddSingleton<ITransactionalGameInputRequestStore>(sp => sp.GetRequiredService<PostgresGameInputRequestService>());
        services.AddScoped<IGameInputRequestDispatcher, GameInputRequestDispatcher>();
        services.AddSingleton<IGameAggregateStateReader, PostgresGameAggregateStateReader>();
        services.AddSingleton<ICommandInbox, PostgresCommandInbox>();
        services.AddSingleton<ITenantWalletReadService, PostgresTenantWalletReadService>();
        services.AddScoped<IGameEffectHandler, PostgresTenantWalletGameEffectHandler>();
        services.AddScoped<IGameEffectHandler, InputRequestGameEffectHandler>();
        services.AddScoped<IAtomicEffectHandler, PostgresTenantWalletAtomicEffectHandler>();
        services.AddSingleton<IAtomicQuotaStore, PostgresAtomicQuotaStore>();
        services.AddSingleton<IAtomicGameAvailability, PostgresAtomicGameAvailability>();
        if (walletRemote)
        {
            services.AddSingleton<IAtomicEconomics, RemoteAtomicEconomics>();
            services.AddSingleton<IAtomicPlayerProtection, RemoteAtomicPlayerProtection>();
            services.AddScoped<IGameEffectHandler, RemoteWalletEconomyEffectHandler>();
        }
        else
        {
            services.AddSingleton<IAtomicEconomics, PostgresAtomicEconomics>();
            services.AddSingleton<IAtomicPlayerProtection, PostgresAtomicPlayerProtection>();
            services.AddSingleton<IWalletAtomicExecutionService, LocalWalletAtomicExecutionService>();
            services.AddScoped<IGameEffectHandler, PostgresWalletEconomyEffectHandler>();
        }
        services.AddSingleton<ITransactionalEventCollector, TransactionalEventCollector>();
        services.AddSingleton<ITransactionalScheduleCollector, TransactionalScheduleCollector>();
        services.AddSingleton<GameExecutionTelemetry>();
        services.AddScoped<IAtomicEffectExecutor, AtomicEffectExecutor>();
        services.AddSingleton<PostgresGameEventOutbox>();
        services.AddSingleton<PostgresGameScheduleOutbox>();
        services.AddSingleton<PostgresGameEffectOutbox>();
        services.AddSingleton<ITransactionalGameEffectOutbox>(sp => sp.GetRequiredService<PostgresGameEffectOutbox>());
        services.AddSingleton<ITransactionalGameExecutionHistoryCollector, TransactionalGameExecutionHistoryCollector>();
        services.AddSingleton<IGameExecutionHistoryReader, PostgresGameExecutionHistoryReader>();
        services.AddScoped(typeof(IAtomicGameExecutor<,,>), typeof(AtomicGameExecutor<,,>));
        services.AddScoped(typeof(IGameStateExecutor<,,>), typeof(GameStateExecutor<,,>));
        services.AddScoped(typeof(IOutcomeOnlyGameExecutor<,,>), typeof(OutcomeOnlyGameExecutor<,,>));
        services.AddScoped<IGameAction<WagerGameCommand, WagerGameState, WagerGameResult>, WagerGameAction>();
        services.AddScoped<GameExecutionDescriptor<WagerGameCommand, WagerGameState, WagerGameResult>, WagerGameDescriptor>();
        services.AddScoped<IGameStateStore<WagerGameCommand, WagerGameState>, PostgresJsonGameStateStore<WagerGameCommand, WagerGameState, WagerGameResult>>();
        services.AddScoped<WagerGameCommandHandler>();
        services.AddScoped<IIntegrationCommandHandler<WagerGameCommand>>(sp => sp.GetRequiredService<WagerGameCommandHandler>());
        services.AddScoped<IWagerGameCommandFactory, WagerGameCommandFactory>();
        services.AddScoped<IWagerSettlementCommandFactory, WagerGameSettlementFactory>();
        services.AddSingleton<WagerGameOutcomeIntegrationBridge>();
        services.AddSingleton<PostgresTelegramOutboxStore>();
        services.AddSingleton<ITelegramOutboxStore>(sp => sp.GetRequiredService<PostgresTelegramOutboxStore>());
        services.AddSingleton<ITelegramOutbox>(sp => sp.GetRequiredService<PostgresTelegramOutboxStore>());
        services.AddSingleton<ITelegramOutboxMonitor>(sp => sp.GetRequiredService<PostgresTelegramOutboxStore>());
        services.AddSingleton<PostgresDiscordOutboxStore>();
        services.AddSingleton<IDiscordOutboxStore>(sp => sp.GetRequiredService<PostgresDiscordOutboxStore>());
        services.AddSingleton<IDiscordOutbox>(sp => sp.GetRequiredService<PostgresDiscordOutboxStore>());
        if (useCapOutboxTransport)
        {
            services.AddSingleton<TelegramOutboxCapRelayService>();
            services.AddSingleton<TelegramOutboxCapReceiptConsumer>();
        }

        if (!walletRemote)
        {
            services.AddSingleton<IEconomicsService, EconomicsService>();
            services.AddSingleton<IWalletReadService, WalletReadService>();
            services.AddSingleton<IWalletAnalyticsService, WalletAnalyticsService>();
        }
        services.AddSingleton<IDistributedGameLock, PostgresDistributedGameLock>();
        services.AddSingleton<IMiniGameSessionStore, PostgresMiniGameSessionStore>();
        services.AddSingleton<IMiniGameRollGateStore, PostgresMiniGameRollGateStore>();
        services.AddRegisteredConfigurationSection<DailyBonusOptions, DailyBonusOptionsValidator>(
            configuration,
            DailyBonusOptions.SectionName);
        if (!walletRemote)
        {
            services.AddSingleton<IDailyBonusService, DailyBonusService>();
            services.AddSingleton<IPlayerProtectionService, PlayerProtectionService>();
        }
        services.AddScoped<PostgresGameAvailabilityService>();
        services.AddScoped<IGameAvailabilityService>(sp => sp.GetRequiredService<PostgresGameAvailabilityService>());
        services.AddScoped<IGameAvailabilityClient>(sp => sp.GetRequiredService<PostgresGameAvailabilityService>());
        services.AddScoped<GameAvailabilityGrpcInterceptor>();

        services.AddRegisteredConfigurationSection<TelegramDiceDailyLimitOptions, TelegramDiceDailyLimitOptionsValidator>(
            configuration,
            TelegramDiceDailyLimitOptions.SectionName);
        services.AddSingleton<ITelegramDiceDailyRollLimiter, TelegramDiceDailyRollLimiter>();

        services.AddSingleton<RuntimeTuningAccessor>();
        services.AddSingleton<IRuntimeTuningAccessor>(sp => sp.GetRequiredService<RuntimeTuningAccessor>());
        services.AddSingleton<IGameDailyQuotaPolicy, TelegramDiceGameDailyQuotaPolicy>();
        services.AddSingleton<RuntimeConfigurationValidator>();
        services.AddScoped<IRuntimeConfigurationService, RuntimeConfigurationService>();
        services.AddScoped<IAdminEffectExecutor, AdminEffectExecutor>();
        services.AddScoped<IAdminEffectHandler, RuntimeConfigurationPatchEffectHandler>();
        services.AddScoped<IAdminEffectHandler, TenantWalletAdjustmentAdminEffectHandler>();
        services.AddScoped<IAdminEffectHandler, TenantWalletSetAdminEffectHandler>();
        if (walletRemote)
        {
            services.AddScoped<IAdminEffectHandler, RemoteWalletAdjustmentAdminEffectHandler>();
            services.AddScoped<IAdminEffectHandler, RemoteWalletSetAdminEffectHandler>();
            services.AddScoped<IAdminEffectHandler, RemoteLedgerRevertAdminEffectHandler>();
        }
        else
        {
            services.AddScoped<IAdminEffectHandler, WalletAdjustmentAdminEffectHandler>();
            services.AddScoped<IAdminEffectHandler, WalletSetAdminEffectHandler>();
            services.AddScoped<IAdminEffectHandler, LedgerRevertAdminEffectHandler>();
        }

        services.AddOptions<ClickHouseOptions>()
            .Bind(configuration.GetSection(ClickHouseOptions.SectionName))
            .Validate(options => !options.Enabled || !string.IsNullOrWhiteSpace(options.Host),
                "ClickHouse:Host is required when ClickHouse:Enabled is true.")
            .Validate(options => options.BufferSize > 0 && options.FlushIntervalMs > 0,
                "ClickHouse buffer and flush interval must be positive.")
            .ValidateOnStart();
        services.AddSingleton<ClickHouseAnalyticsService>();
        services.AddSingleton<IAnalyticsService>(sp => sp.GetRequiredService<ClickHouseAnalyticsService>());
        services.AddHostedService(sp => sp.GetRequiredService<ClickHouseAnalyticsService>());
        services.AddSingleton<IAnalyticsQueryService, ClickHouseAnalyticsQueryService>();

        services.AddSingleton<IEventSerializer, JsonEventSerializer>();
        services.AddSingleton<IEventStore, PostgresEventStore>();
        services.AddScoped<EventDispatcher>();
        services.AddScoped<IEventReplayService, EventReplayService>();
        services.AddScoped<IReadOnlyEventReplayService, ReadOnlyEventReplayService>();
        services.AddSingleton<IEconomySimulationService, EconomySimulationService>();
        services.AddScoped<IRandomOutcomeGenerator, PostgresRandomOutcomeGenerator>();
        services.AddScoped<IEventDispatchRetryService, EventDispatchRetryService>();
        services.AddSingleton<IEventDispatchFailureStore, PostgresEventDispatchFailureStore>();
        services.AddSingleton(typeof(ISnapshotStore<>), typeof(PostgresSnapshotStore<>));
        services.AddSingleton<IEventLog, PostgresEventLog>();
        services.AddSingleton<EventLogSubscriber>();
        services.AddSingleton<ClickHouseEventMirror>();
        services.AddScoped<BotFramework.Contracts.Operations.IWagerWorkflowTimelineReader, BotFramework.Host.Admin.Operations.PostgresWagerWorkflowTimelineReader>();
        services.AddSingleton<IBackgroundJobStatusService, BackgroundJobStatusService>();
        services.AddScoped<BotFramework.Contracts.Operations.IOperationsAdminService, BotFramework.Host.Admin.Operations.OperationsAdminService>();

        services.AddHostedService<ModuleMigrationRunner>();
        if (useCapOutboxTransport)
            services.AddHostedService(sp => sp.GetRequiredService<TelegramOutboxCapRelayService>());
        services.AddQuartzGameScheduling(pgConnStr, schedulerName);
        services.AddHostedService<EventAnalyticsBackfillService>();
        services.AddHostedService<GameEventOutboxDispatcher>();
        services.AddHostedService<GameScheduleOutboxDispatcher>();
        services.AddHostedService<GameEffectOutboxDispatcher>();
        services.AddHostedService(sp => sp.GetRequiredService<RuntimeTuningAccessor>());
        // The catch-up service depends on the locally owned wallet implementation.
        // Game backends use the wallet over gRPC and therefore do not register
        // IDailyBonusService; registering the hosted service there would prevent
        // the whole process from starting.
        if (!walletRemote)
            services.AddHostedService<DailyBonusCatchUpHostedService>();

        services.AddHostedService<BackgroundJobRunner>();
        services.AddQuartzRecurringCommandBootstrapper();

        if (redisEnabled)
        {
            services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConn!));
            services.AddSingleton<RedisCacheStore>();
            services.AddSingleton<ICacheStore>(sp => sp.GetRequiredService<RedisCacheStore>());
            services.AddSingleton<ICacheStoreInvalidator>(sp => sp.GetRequiredService<RedisCacheStore>());
        }

        return new BotFrameworkBuilder(services, configuration);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// BotFrameworkBuilder — the ASP.NET-Core-style composition surface for a Host.
//
// Usage:
//
//   var builder = WebApplication.CreateBuilder(args);
//
//   builder.AddBotFramework()
//       .AddModule<DiceModule>()
//       .AddModule<PokerModule>();
//
//   var app = builder.Build();
//   app.UseBotFramework();
//   app.Run();
//
// Why a builder and not a single `AddModules(params Type[])` call:
//   modules declare fluent options (future: AddModule<T>().WithOptions(...))
//   and the chainable style mirrors ASP.NET Core's AddAuthentication/AddJwtBearer
//   pattern the user already knows. New module authors get a familiar shape.
//
// Phase ordering inside the builder:
//   1. AddBotFramework() — registers framework singletons (Telegram client,
//      UpdateRouter, UpdatePipeline, the three default update middlewares,
//      BotHostedService) and constructs the ModuleServiceCollectionAdapter
//      modules will see during ConfigureServices.
//   2. AddModule<T>() — instantiates the module (parameterless ctor),
//      registers it as IModule in DI, calls module.ConfigureServices(adapter)
//      so its handlers/aggregates/projections/commands are wired, and folds
//      its locales/commands/migrations into the builder-local aggregate.
//   3. builder.Build() — the container is built. LoadedModules is resolved
//      from a singleton factory that closes over the aggregate built in (2),
//      so by the time BotHostedService asks for it every module is there.
// ─────────────────────────────────────────────────────────────────────────────

using BotFramework.Host.Composition;
using BotFramework.Host.Pipeline;
using BotFramework.Host.Services;
using BotFramework.Sdk;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Telegram.Bot;

namespace BotFramework.Host.Composition;

public interface IBotFrameworkBuilder
{
    IServiceCollection Services { get; }
    IConfiguration Configuration { get; }

    /// Registers a module. The module type must have a parameterless ctor —
    /// same constraint the sketch's ModuleLoader used. ConfigureServices is
    /// invoked eagerly against the shared IServiceCollection via the adapter.
    IBotFrameworkBuilder AddModule<TModule>() where TModule : class, IModule, new();
}

internal sealed class BotFrameworkBuilder : IBotFrameworkBuilder
{
    private readonly List<IModule> _modules = [];
    private readonly Dictionary<string, Dictionary<string, string>> _locales = new();
    private readonly List<BotCommand> _botCommands = [];
    private readonly List<IModuleMigrations> _migrations = [];
    private readonly ModuleRegistrations _registrations;
    private readonly ModuleServiceCollectionAdapter _adapter;

    public IServiceCollection Services { get; }
    public IConfiguration Configuration { get; }

    public BotFrameworkBuilder(IServiceCollection services, IConfiguration configuration)
    {
        Services = services;
        Configuration = configuration;
        _registrations = new ModuleRegistrations();
        _adapter = new ModuleServiceCollectionAdapter(services, configuration, _registrations);

        services.AddSingleton(_registrations);

        // Factory closes over the mutable aggregate lists. LoadedModules is
        // resolved at first use (by BotHostedService during StartAsync), at
        // which point every AddModule<T>() call has already completed because
        // the container has been built.
        services.AddSingleton<LoadedModules>(_ => new LoadedModules(
            _modules,
            _locales,
            _botCommands,
            _migrations));
    }

    public IBotFrameworkBuilder AddModule<TModule>() where TModule : class, IModule, new()
    {
        var module = new TModule();

        Services.AddSingleton<IModule>(module);
        module.ConfigureServices(_adapter);

        if (module.GetMigrations() is { } m)
            _migrations.Add(m);

        foreach (var bundle in module.GetLocales())
        {
            if (!_locales.TryGetValue(bundle.CultureCode, out var dict))
                _locales[bundle.CultureCode] = dict = new();

            foreach (var (key, value) in bundle.Strings)
                dict[$"{module.Id}.{key}"] = value;
        }

        _botCommands.AddRange(module.GetBotCommands());
        _modules.Add(module);

        return this;
    }
}

public static class BotFrameworkBuilderExtensions
{
    /// Registers framework core services and returns a builder for fluent
    /// module registration. Call before any AddModule<T>().
    public static IBotFrameworkBuilder AddBotFramework(this IHostApplicationBuilder builder)
    {
        var services = builder.Services;
        var configuration = builder.Configuration;

        DapperTypeHandlers.Register();

        services.AddRazorPages();

        services.Configure<BotFrameworkOptions>(configuration.GetSection(BotFrameworkOptions.SectionName));

        services.AddSingleton<ITelegramBotClient>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<BotFrameworkOptions>>().Value;
            if (string.IsNullOrWhiteSpace(opts.Token))
                throw new InvalidOperationException(
                    "BotFrameworkOptions.Token is not configured. Set Bot:Token in configuration.");
            return new TelegramBotClient(opts.Token);
        });

        services.AddSingleton<UpdateRouter>();
        services.AddSingleton<UpdatePipeline>();

        // Default update middleware chain in registration order =
        // execution order, outermost first: Exception -> Logging -> RateLimit.
        services.AddSingleton<IUpdateMiddleware, ExceptionMiddleware>();
        services.AddSingleton<IUpdateMiddleware, LoggingMiddleware>();
        services.AddSingleton<IUpdateMiddleware, RateLimitMiddleware>();

        // Command bus + cross-module domain event bus. Middleware is picked up
        // from DI — modules add theirs through IModuleServiceCollection and the
        // Host can supply additional ICommandMiddleware singletons before
        // AddBotFramework returns.
        services.AddSingleton<ICommandBus, CommandBus>();
        services.AddSingleton<IDomainEventBus, InProcessEventBus>();
        services.AddHostedService<EventSubscriptionInitializer>();

        services.AddSingleton<HealthEndpoint>();
        services.AddSingleton<ILocalizer, Localizer>();
        services.AddSingleton<INpgsqlConnectionFactory, NpgsqlConnectionFactory>();

        // Cross-cutting services every module shares.
        services.AddSingleton<IEconomicsService, EconomicsService>();

        services.Configure<ClickHouseOptions>(configuration.GetSection(ClickHouseOptions.SectionName));
        services.AddSingleton<ClickHouseAnalyticsService>();
        services.AddSingleton<IAnalyticsService>(sp => sp.GetRequiredService<ClickHouseAnalyticsService>());
        services.AddHostedService(sp => sp.GetRequiredService<ClickHouseAnalyticsService>());

        // Event sourcing stack. Registered as singletons because they hold no
        // per-request state and all I/O runs on short-lived pooled connections.
        services.AddSingleton<IEventSerializer, JsonEventSerializer>();
        services.AddSingleton<IEventStore, PostgresEventStore>();
        services.AddSingleton(typeof(ISnapshotStore<>), typeof(PostgresSnapshotStore<>));
        services.AddSingleton<IEventLog, PostgresEventLog>();
        services.AddSingleton<EventLogSubscriber>();
        services.AddSingleton<ClickHouseEventMirror>();

        // Must run before BotHostedService so the schema is in place before
        // polling starts. Generic Host awaits hosted services in registration
        // order, so registering the migration runner first does the trick.
        services.AddHostedService<ModuleMigrationRunner>();

        services.AddHostedService<BackgroundJobRunner>();

        services.AddHostedService<BotHostedService>();

        return new BotFrameworkBuilder(services, configuration);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// IModule — the contract every game implements.
//
// A module is a self-contained feature: its DI registrations, entity
// configurations, locales, route handlers, and config options all live here.
// Adding a game to a Host = reference the module's assembly + register it once.
// ─────────────────────────────────────────────────────────────────────────────

namespace BotFramework.Sdk;

public interface IModule
{
    /// Stable machine identifier: "poker", "sh", "blackjack". Used as the config
    /// section key (Games:<Id>), the event-type prefix, and the admin URL segment.
    string Id { get; }

    /// Human-readable name for menus and admin UI. Localized via GetLocales.
    string DisplayName { get; }

    /// Semver for the module. Surfaced to admin UI; also gates migrations.
    string Version { get; }

    /// Called once at Host startup. Bind options, register services, register
    /// handlers, register repositories. Modules MUST NOT touch services other
    /// modules own — only the Host-provided abstractions (IRepository<T>,
    /// IEventStore, IEconomics, IAnalytics, etc.).
    void ConfigureServices(IModuleServiceCollection services);

    /// Per-module schema. Host applies migrations against the module's own
    /// tracking table (__module_migrations) before any service starts. Null
    /// for modules with no tables of their own (e.g. a pure aggregator that
    /// only reads shared tables). See sdk/ModuleMigrations.cs.
    IModuleMigrations? GetMigrations() => null;

    /// Returns the module's localization resources. Host merges them into its
    /// central resource manager keyed on (cultureCode, moduleId, resourceKey).
    IReadOnlyList<LocaleBundle> GetLocales();

    /// Optional. Returns Telegram bot-menu commands this module contributes
    /// (shown in the blue "/" menu). Host aggregates across modules and calls
    /// SetMyCommands once.
    IReadOnlyList<BotCommand> GetBotCommands() => [];

    /// Optional. Called when the Host is shutting down. Modules with background
    /// work (timeout sweepers, flushes) cleanly stop here.
    Task ShutdownAsync(CancellationToken ct) => Task.CompletedTask;
}

// ─────────────────────────────────────────────────────────────────────────────
// Host-supplied abstractions that modules bind against. Modules never see the
// concrete DI container type — they see these narrower views. That keeps them
// portable across Host implementations and testable in isolation.
// ─────────────────────────────────────────────────────────────────────────────

public interface IModuleServiceCollection
{
    /// Binds a section of Host config (e.g. Games:poker) into a typed options
    /// class. Host wraps IConfiguration so modules don't take a dependency on
    /// Microsoft.Extensions.Configuration directly.
    IModuleServiceCollection BindOptions<TOptions>(string configSection) where TOptions : class;

    IModuleServiceCollection AddScoped<TService, TImpl>() where TImpl : class, TService;
    IModuleServiceCollection AddSingleton<TService, TImpl>() where TImpl : class, TService;

    /// Registers a domain aggregate with its persistence strategy. Host picks
    /// the right IRepository<TAggregate> implementation based on the strategy.
    IModuleServiceCollection RegisterAggregate<TAggregate>(PersistenceStrategy strategy)
        where TAggregate : IAggregateRoot;

    /// Registers an IUpdateHandler type; Host adds it to the attribute-scanned
    /// handler set so routing picks up the handler's [Command]/[CallbackPrefix]
    /// attributes automatically.
    IModuleServiceCollection AddHandler<THandler>() where THandler : class;

    /// Registers a read-model projection. Host wires it into the event
    /// dispatcher so events this projection subscribes to flow through it in
    /// the same transaction as the event-store append.
    IModuleServiceCollection AddProjection<TProjection>() where TProjection : class, IProjection;

    /// Registers an admin page. Host mounts it at /admin/<moduleId>/<page.Route>
    /// after AdminWebToken middleware passes.
    IModuleServiceCollection AddAdminPage<TPage>() where TPage : class, IAdminPage;

    /// Registers a background job. Host hosts it as an IHostedService, runs
    /// RunAsync on startup, signals cancellation on shutdown.
    IModuleServiceCollection AddBackgroundJob<TJob>() where TJob : class, IBackgroundJob;

    /// Registers a command handler. Bus dispatches through every middleware
    /// registered with AddCommandMiddleware before reaching this handler.
    IModuleServiceCollection AddCommandHandler<TCommand, THandler>()
        where TCommand : ICommand
        where THandler : class, ICommandHandler<TCommand>;

    /// Registers a command-pipeline middleware. Host-level concerns (logging,
    /// metrics, rate-limit) come from the Host; modules add their own only
    /// when they need per-module behavior that can't be parameterized.
    IModuleServiceCollection AddCommandMiddleware<TMiddleware>() where TMiddleware : class, ICommandMiddleware;

    /// Subscribes a handler to a cross-module domain-event pattern. Pattern
    /// grammar: exact ("sh.game_ended"), module wildcard ("sh.*"), action
    /// wildcard ("*.game_ended"), total wildcard ("*").
    IModuleServiceCollection AddDomainEventSubscription<TSubscriber>(string eventTypePattern)
        where TSubscriber : class, IDomainEventSubscriber;

    /// Registers a health check. Host aggregates them at /health (liveness +
    /// readiness). Slow checks belong in the background job, not here.
    IModuleServiceCollection AddHealthCheck<TCheck>() where TCheck : class, IHealthCheck;
}

/// Long-running worker registered by a module. Host starts it on app start
/// and cancels the token on shutdown. Exceptions do NOT bring the Host down —
/// the runner logs and restarts with backoff.
public interface IBackgroundJob
{
    string Name { get; }
    Task RunAsync(CancellationToken stoppingToken);
}

public enum PersistenceStrategy
{
    /// Aggregate is mutated in place and persisted as a row. Default.
    Classical,
    /// Aggregate emits domain events which are appended to an event store.
    /// State is rebuilt by replaying events. Opt in per aggregate.
    EventSourced,
}

public sealed record LocaleBundle(string CultureCode, IReadOnlyDictionary<string, string> Strings);
public sealed record BotCommand(string Command, string DescriptionKey);

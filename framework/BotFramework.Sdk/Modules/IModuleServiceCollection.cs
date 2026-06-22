// ─────────────────────────────────────────────────────────────────────────────
// IModule — the contract every game implements.
//
// A module is a self-contained feature: its DI registrations, entity
// configurations, locales, route handlers, and config options all live here.
// Adding a game to a Host = reference the module's assembly + register it once.
// ─────────────────────────────────────────────────────────────────────────────

namespace BotFramework.Sdk;
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

    /// <summary>Registers a concrete type as its own singleton implementation (e.g. background workers).</summary>
    IModuleServiceCollection AddSingleton<TImplementation>() where TImplementation : class;

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

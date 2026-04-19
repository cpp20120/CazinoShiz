// ─────────────────────────────────────────────────────────────────────────────
// ModuleServiceCollectionAdapter — the surface modules see during
// ConfigureServices(). Every call forwards to the real IServiceCollection
// (for DI wiring) and, where relevant, records the registration in a
// ModuleRegistrations bag the Host consumes later.
//
// Why the two-phase split:
//   • DI registration must happen now — `IServiceCollection` is frozen once
//     `builder.Build()` runs.
//   • The CommandBus' handler map, the InProcessEventBus' subscription map,
//     the EventDispatcher's projection list, the AdminMount's page index —
//     all need to be built AFTER modules have had a chance to declare their
//     intent. Those consumers resolve ModuleRegistrations at startup and
//     walk its lists.
//
// The bag carries registration metadata only — no instances. Everything is
// still resolved from DI at request time so Scoped lifetimes work correctly.
// ─────────────────────────────────────────────────────────────────────────────

using BotFramework.Sdk;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace BotFramework.Host;

public sealed class ModuleServiceCollectionAdapter(
    IServiceCollection services,
    IConfiguration configuration,
    ModuleRegistrations registrations) : IModuleServiceCollection
{
    public IModuleServiceCollection BindOptions<TOptions>(string configSection) where TOptions : class
    {
        services.Configure<TOptions>(configuration.GetSection(configSection));
        return this;
    }

    public IModuleServiceCollection AddScoped<TService, TImpl>() where TImpl : class, TService
    {
        services.AddScoped(typeof(TService), typeof(TImpl));
        return this;
    }

    public IModuleServiceCollection AddSingleton<TService, TImpl>() where TImpl : class, TService
    {
        services.AddSingleton(typeof(TService), typeof(TImpl));
        return this;
    }

    public IModuleServiceCollection RegisterAggregate<TAggregate>(PersistenceStrategy strategy)
        where TAggregate : IAggregateRoot
    {
        registrations.Aggregates.Add(new AggregateRegistration(typeof(TAggregate), strategy));
        return this;
    }

    public IModuleServiceCollection AddHandler<THandler>() where THandler : class
    {
        // Handlers are Scoped. UpdateRouter resolves by Type via reflection at dispatch.
        services.AddScoped(typeof(THandler));
        return this;
    }

    public IModuleServiceCollection AddProjection<TProjection>() where TProjection : class, IProjection
    {
        services.AddScoped<TProjection>();
        services.AddScoped<IProjection>(sp => sp.GetRequiredService<TProjection>());
        registrations.Projections.Add(typeof(TProjection));
        return this;
    }

    public IModuleServiceCollection AddAdminPage<TPage>() where TPage : class, IAdminPage
    {
        services.AddScoped<TPage>();
        services.AddScoped<IAdminPage>(sp => sp.GetRequiredService<TPage>());
        registrations.AdminPages.Add(typeof(TPage));
        return this;
    }

    public IModuleServiceCollection AddBackgroundJob<TJob>() where TJob : class, IBackgroundJob
    {
        services.AddSingleton<TJob>();
        registrations.BackgroundJobs.Add(typeof(TJob));
        return this;
    }

    public IModuleServiceCollection AddCommandHandler<TCommand, THandler>()
        where TCommand : ICommand
        where THandler : class, ICommandHandler<TCommand>
    {
        services.AddScoped<ICommandHandler<TCommand>, THandler>();
        registrations.CommandHandlers[typeof(TCommand)] = typeof(THandler);
        return this;
    }

    public IModuleServiceCollection AddCommandMiddleware<TMiddleware>()
        where TMiddleware : class, ICommandMiddleware
    {
        services.AddSingleton<TMiddleware>();
        services.AddSingleton<ICommandMiddleware>(sp => sp.GetRequiredService<TMiddleware>());
        registrations.CommandMiddleware.Add(typeof(TMiddleware));
        return this;
    }

    public IModuleServiceCollection AddDomainEventSubscription<TSubscriber>(string eventTypePattern)
        where TSubscriber : class, IDomainEventSubscriber
    {
        // Subscribers are singletons because InProcessEventBus holds instance
        // references built at startup. If a subscriber needs per-event scoped
        // state, it takes IServiceProvider and creates its own scope inside
        // HandleAsync.
        services.AddSingleton<TSubscriber>();
        registrations.EventSubscriptions.Add(new EventSubscription(eventTypePattern, typeof(TSubscriber)));
        return this;
    }

    public IModuleServiceCollection AddHealthCheck<TCheck>() where TCheck : class, IHealthCheck
    {
        services.AddSingleton<TCheck>();
        services.AddSingleton<IHealthCheck>(sp => sp.GetRequiredService<TCheck>());
        registrations.HealthChecks.Add(typeof(TCheck));
        return this;
    }
}

/// Mutable bag populated during ModuleLoader.LoadAll. Registered as a singleton
/// so the CommandBus, InProcessEventBus, EventDispatcher, AdminMount, and the
/// background-job runner all see the same view at app start.
public sealed class ModuleRegistrations
{
    public List<AggregateRegistration> Aggregates { get; } = [];
    public List<Type> Projections { get; } = [];
    public List<Type> AdminPages { get; } = [];
    public List<Type> BackgroundJobs { get; } = [];
    public Dictionary<Type, Type> CommandHandlers { get; } = new();
    public List<Type> CommandMiddleware { get; } = [];
    public List<EventSubscription> EventSubscriptions { get; } = [];
    public List<Type> HealthChecks { get; } = [];
}

public sealed record AggregateRegistration(Type AggregateType, PersistenceStrategy Strategy);
public sealed record EventSubscription(string EventTypePattern, Type SubscriberType);

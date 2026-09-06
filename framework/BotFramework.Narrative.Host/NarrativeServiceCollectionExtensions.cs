using BotFramework.Host.Execution;
using BotFramework.Sdk.Execution;
using BotFramework.Sdk.Modules.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BotFramework.Narrative.Host;

/// <summary>
/// Connects transport-independent narrative effects to one frontend-owned sink.
/// The sink can be implemented by Web, Telegram, a queue worker or tests.
/// </summary>
public static class NarrativeServiceCollectionExtensions
{
    /// <summary>
    /// Registers the PostgreSQL projection for flags, checkpoints and active
    /// choices. It is useful to a Web, Telegram or test frontend alike.
    /// </summary>
    public static IServiceCollection AddNarrativePersistence(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<PostgresNarrativeProjectionStore>();
        services.TryAddSingleton<INarrativeProjectionStore>(
            serviceProvider => serviceProvider.GetRequiredService<PostgresNarrativeProjectionStore>());
        services.TryAddSingleton<INarrativeProjectionWriter>(
            serviceProvider => serviceProvider.GetRequiredService<PostgresNarrativeProjectionStore>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IModuleMigrations, NarrativeProjectionMigrations>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IGameRuntimeCapabilityProvider, NarrativePersistenceRuntimeCapabilityProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<IGameEffectHandler, NarrativeGameEffectHandler>());
        return services;
    }

    public static IServiceCollection AddNarrativeEffectSink<TSink>(this IServiceCollection services)
        where TSink : class, INarrativeEffectSink
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddNarrativePersistence();
        services.TryAddScoped<INarrativeEffectSink, TSink>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IGameRuntimeCapabilityProvider, NarrativeRuntimeCapabilityProvider>());
        return services;
    }
}

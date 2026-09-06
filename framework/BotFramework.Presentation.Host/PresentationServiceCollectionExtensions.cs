using BotFramework.Host.Execution;
using BotFramework.Sdk.Execution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BotFramework.Presentation.Host;

/// <summary>Connects transport-independent presentation effects to one frontend sink.</summary>
public static class PresentationServiceCollectionExtensions
{
    /// <summary>
    /// Registers a frontend-owned sink and the generic Host handler. Effects
    /// remain durable and are therefore delivered from the transactional outbox.
    /// </summary>
    public static IServiceCollection AddPresentationEffectSink<TSink>(this IServiceCollection services)
        where TSink : class, IPresentationEffectSink
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<IPresentationEffectSink, TSink>();
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<IGameEffectHandler, PresentationGameEffectHandler>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IGameRuntimeCapabilityProvider, PresentationRuntimeCapabilityProvider>());
        return services;
    }
}

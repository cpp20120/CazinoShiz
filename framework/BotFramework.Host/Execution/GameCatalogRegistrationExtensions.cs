using BotFramework.Sdk.Execution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BotFramework.Host.Execution;

/// <summary>Registers a discoverable game manifest without coupling it to a transport.</summary>
public static class GameCatalogRegistrationExtensions
{
    public static IServiceCollection AddGameDefinition(this IServiceCollection services, GameDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(definition);

        services.AddSingleton(definition);
        services.TryAddSingleton<IGameCatalog, GameCatalog>();
        return services;
    }
}

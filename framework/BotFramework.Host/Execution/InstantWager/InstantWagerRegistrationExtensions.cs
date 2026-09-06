using BotFramework.Sdk.Execution.InstantWager;
using Microsoft.Extensions.DependencyInjection;

namespace BotFramework.Host.Execution.InstantWager;

/// <summary>Registers an instant-wager definition, execution pipeline and game manifest.</summary>
public static class InstantWagerRegistrationExtensions
{
    public static IServiceCollection AddInstantWagerGame<TGame, TOutcome>(
        this IServiceCollection services,
        InstantWagerDefinition<TGame, TOutcome> definition)
        where TGame : IInstantWagerGame
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(definition);

        var definitionType = typeof(InstantWagerDefinition<TGame, TOutcome>);
        if (services.Any(descriptor => descriptor.ServiceType == definitionType))
        {
            throw new InvalidOperationException(
                $"An instant-wager definition is already registered for '{typeof(TGame).FullName}'.");
        }

        services.AddSingleton(definition);
        services.AddGameDefinition(definition.Game);
        services.AddAtomicStatelessGameAction<
            InstantWagerCommand<TGame>,
            InstantWagerAction<TGame, TOutcome>,
            InstantWagerResult<TOutcome>,
            InstantWagerDescriptor<TGame, TOutcome>>();
        services.AddScoped<
            IInstantWagerGameExecutor<TGame, TOutcome>,
            InstantWagerGameExecutor<TGame, TOutcome>>();
        return services;
    }
}

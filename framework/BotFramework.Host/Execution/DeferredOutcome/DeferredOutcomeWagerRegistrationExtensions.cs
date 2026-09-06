using BotFramework.Sdk.Execution.DeferredOutcome;
using Microsoft.Extensions.DependencyInjection;

namespace BotFramework.Host.Execution.DeferredOutcome;

public static class DeferredOutcomeWagerRegistrationExtensions
{
    /// <summary>
    /// Registers the complete wager lifecycle: generic actions, descriptors,
    /// framework JSONB state stores and a compact application-service executor.
    /// A module supplies only a marker, outcome type and deterministic definition.
    /// </summary>
    public static IServiceCollection AddDeferredOutcomeWagerGame<TGame, TOutcome>(
        this IServiceCollection services,
        DeferredOutcomeWagerDefinition<TGame, TOutcome> definition)
        where TGame : IDeferredOutcomeWagerGame
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(definition);

        var definitionType = typeof(DeferredOutcomeWagerDefinition<TGame, TOutcome>);
        if (services.Any(descriptor => descriptor.ServiceType == definitionType))
        {
            throw new InvalidOperationException(
                $"A deferred-outcome wager definition is already registered for '{typeof(TGame).FullName}'.");
        }

        services.AddSingleton(definition);
        services.AddGameDefinition(definition.Game);
        RegisterPlace<TGame, TOutcome>(services);
        RegisterResolve<TGame, TOutcome>(services);
        RegisterAbort<TGame, TOutcome>(services);
        services.AddScoped<IDeferredOutcomeWagerGameExecutor<TGame, TOutcome>, DeferredOutcomeWagerGameExecutor<TGame, TOutcome>>();
        return services;
    }

    private static void RegisterPlace<TGame, TOutcome>(IServiceCollection services)
        where TGame : IDeferredOutcomeWagerGame
    {
        services.AddAtomicJsonGameAction<
            DeferredOutcomeWagerPlaceCommand<TGame, TOutcome>,
            DeferredOutcomeWagerState,
            DeferredOutcomeWagerPlaceAction<TGame, TOutcome>,
            DeferredOutcomeWagerPlaceResult,
            DeferredOutcomeWagerPlaceDescriptor<TGame, TOutcome>>();
    }

    private static void RegisterResolve<TGame, TOutcome>(IServiceCollection services)
        where TGame : IDeferredOutcomeWagerGame
    {
        services.AddAtomicJsonGameAction<
            DeferredOutcomeWagerResolveCommand<TGame, TOutcome>,
            DeferredOutcomeWagerState,
            DeferredOutcomeWagerResolveAction<TGame, TOutcome>,
            DeferredOutcomeWagerResolveResult<TOutcome>,
            DeferredOutcomeWagerResolveDescriptor<TGame, TOutcome>>();
    }

    private static void RegisterAbort<TGame, TOutcome>(IServiceCollection services)
        where TGame : IDeferredOutcomeWagerGame
    {
        services.AddAtomicJsonGameAction<
            DeferredOutcomeWagerAbortCommand<TGame, TOutcome>,
            DeferredOutcomeWagerState,
            DeferredOutcomeWagerAbortAction<TGame, TOutcome>,
            DeferredOutcomeWagerAbortResult,
            DeferredOutcomeWagerAbortDescriptor<TGame, TOutcome>>();
    }
}

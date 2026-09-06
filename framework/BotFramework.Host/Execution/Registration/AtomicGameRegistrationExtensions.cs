using BotFramework.Scheduling.Abstractions;
using BotFramework.Sdk.Execution;

namespace BotFramework.Host.Execution;

/// <summary>Common DI registrations for atomic game actions and their state stores.</summary>
public static class AtomicGameRegistrationExtensions
{
    public static IServiceCollection AddAtomicJsonGameAction<
        TCommand,
        TState,
        TAction,
        TResult,
        TDescriptor>(
        this IServiceCollection services,
        bool registerScheduledCommand = false)
        where TState : class, IVersionedGameState
        where TAction : class, IGameAction<TCommand, TState, TResult>
        where TDescriptor : GameExecutionDescriptor<TCommand, TState, TResult>
    {
        RegisterAction<TCommand, TState, TAction, TResult, TDescriptor>(services);
        services.AddScoped<
            IGameStateStore<TCommand, TState>,
            PostgresJsonGameStateStore<TCommand, TState, TResult>>();
        if (registerScheduledCommand)
        {
            services.AddScoped<
                IScheduledCommand,
                AtomicGameScheduledCommand<TCommand, TState, TResult>>();
        }

        return services;
    }

    public static IServiceCollection AddAtomicGameAction<
        TCommand,
        TState,
        TAction,
        TResult,
        TDescriptor,
        TStateStore>(
        this IServiceCollection services,
        bool registerScheduledCommand = false)
        where TAction : class, IGameAction<TCommand, TState, TResult>
        where TDescriptor : GameExecutionDescriptor<TCommand, TState, TResult>
        where TStateStore : class, IGameStateStore<TCommand, TState>
    {
        RegisterAction<TCommand, TState, TAction, TResult, TDescriptor>(services);
        services.AddScoped<IGameStateStore<TCommand, TState>, TStateStore>();
        if (registerScheduledCommand)
        {
            services.AddScoped<
                IScheduledCommand,
                AtomicGameScheduledCommand<TCommand, TState, TResult>>();
        }

        return services;
    }

    public static IServiceCollection AddAtomicStatelessGameAction<
        TCommand,
        TAction,
        TResult,
        TDescriptor>(this IServiceCollection services)
        where TAction : class, IGameAction<TCommand, NoGameState, TResult>
        where TDescriptor : GameExecutionDescriptor<TCommand, NoGameState, TResult>
    {
        RegisterAction<TCommand, NoGameState, TAction, TResult, TDescriptor>(services);
        services.AddScoped<IGameStateStore<TCommand, NoGameState>, StatelessGameStateStore<TCommand>>();
        return services;
    }

    private static void RegisterAction<TCommand, TState, TAction, TResult, TDescriptor>(IServiceCollection services)
        where TAction : class, IGameAction<TCommand, TState, TResult>
        where TDescriptor : GameExecutionDescriptor<TCommand, TState, TResult>
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<TDescriptor>();
        services.AddScoped<GameExecutionDescriptor<TCommand, TState, TResult>>(
            provider => provider.GetRequiredService<TDescriptor>());
        services.AddScoped<IGameAction<TCommand, TState, TResult>, TAction>();
    }
}

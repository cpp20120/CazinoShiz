using BotFramework.Sdk.Execution;

namespace BotFramework.Host.Execution;

public static class TurnBasedGameRegistrationExtensions
{
    /// <summary>
    /// Registers a turn action with framework-owned JSONB versioned state persistence.
    /// A game can replace IGameStateStore after this call when it needs a relational model.
    /// </summary>
    public static IServiceCollection AddAtomicTurnBasedGameAction<
        TCommand,
        TState,
        TAction,
        TResult,
        TDescriptor>(this IServiceCollection services)
        where TState : class, IVersionedGameState
        where TAction : class, IGameAction<TCommand, TState, TResult>
        where TDescriptor : GameExecutionDescriptor<TCommand, TState, TResult>
    {
        return services.AddAtomicJsonGameAction<TCommand, TState, TAction, TResult, TDescriptor>(
            registerScheduledCommand: true);
    }
}

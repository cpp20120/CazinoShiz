using BotFramework.Sdk.Execution;

namespace BotFramework.Host.Execution;

public abstract class GameExecutionDescriptor<TCommand, TState, TResult>
{
    public Type StateType => typeof(TState);

    public Type ResultType => typeof(TResult);

    public abstract string GameId { get; }

    public abstract string CommandId(TCommand command);

    public abstract string AggregateId(TCommand command);

    public abstract long ChatId(TCommand command);

    public abstract string DisplayName(TCommand command);

    public abstract WalletIdentity Wallet(TCommand command);

    public virtual bool UsesPrimaryWallet => true;

    public virtual IReadOnlyList<string> AdditionalLockKeys(TCommand command) => [];

    public virtual IReadOnlyList<QuotaIdentity> Quotas(TCommand command, DateTimeOffset utcNow) => [];

    public virtual IReadOnlyList<string> EntropyNames => [];

    /// <summary>
    /// Runtime features declared by this game. Legacy descriptors intentionally
    /// return an empty set so adopting capability validation is opt-in.
    /// </summary>
    public virtual GameCapabilitySet RequiredCapabilities => GameCapabilitySet.Empty;

    /// <summary>
    /// Creates revision zero when the framework JSON state store cannot find an aggregate.
    /// Stateful descriptors using that store must override this method.
    /// </summary>
    public virtual TState CreateInitialState(TCommand command) =>
        throw new InvalidOperationException(
            $"Descriptor '{GetType().Name}' does not define an initial state for '{typeof(TState).Name}'.");
}

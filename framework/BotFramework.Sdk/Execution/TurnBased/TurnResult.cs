namespace BotFramework.Sdk.Execution;

/// <summary>
/// Output of a pure turn rule. It keeps game-owned state, declarative effects
/// and framework-owned turn progression in one value without binding either
/// rules or effects to a transport.
/// </summary>
public sealed record TurnResult<TState, TPlayerId>
    where TPlayerId : notnull
{
    public TurnResult(
        TState state,
        GameEffectSet effects,
        TurnCompletion<TPlayerId> completion)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(effects);
        ArgumentNullException.ThrowIfNull(completion);

        State = state;
        Effects = completion is TurnCompletion<TPlayerId>.WaitForInputTurn wait
            ? effects.WithCustom(wait.Input)
            : effects;
        Completion = completion;
    }

    /// <summary>New game-owned domain state, not including the turn-engine snapshot.</summary>
    public TState State { get; }

    /// <summary>All effects to commit atomically with the new state.</summary>
    public GameEffectSet Effects { get; }

    /// <summary>Instruction that the turn engine applies after the rule result.</summary>
    public TurnCompletion<TPlayerId> Completion { get; }
}

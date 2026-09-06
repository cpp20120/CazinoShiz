namespace BotFramework.Sdk.Execution;

/// <summary>
/// Output of a pure phase rule. It combines game-owned state, effects and the
/// framework-owned progression selected for the current phase.
/// </summary>
public sealed record RoundResult<TState, TPhase>
    where TPhase : notnull
{
    public RoundResult(
        TState state,
        GameEffectSet effects,
        RoundCompletion<TPhase> completion)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(effects);
        ArgumentNullException.ThrowIfNull(completion);

        State = state;
        Effects = completion is RoundCompletion<TPhase>.WaitForInput wait
            ? effects.WithCustom(wait.Input)
            : effects;
        Completion = completion;
    }

    public TState State { get; }

    public GameEffectSet Effects { get; }

    public RoundCompletion<TPhase> Completion { get; }
}

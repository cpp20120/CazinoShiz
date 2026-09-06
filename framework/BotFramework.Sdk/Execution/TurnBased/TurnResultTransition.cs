namespace BotFramework.Sdk.Execution;

/// <summary>
/// Atomically-shaped result of applying a <see cref="TurnResult{TState,TPlayerId}"/>
/// to a turn snapshot. A rejected transition deliberately returns the original
/// domain state and no effects, preventing stale input from leaking mutations.
/// </summary>
public sealed record TurnResultTransition<TState, TPlayerId>(
    TurnEngineTransitionStatus Status,
    TState State,
    TurnEngineState<TPlayerId> Turns,
    GameEffectSet Effects,
    TurnCompletion<TPlayerId>? Completion = null,
    TurnEngineRejection? Rejection = null)
    where TPlayerId : notnull
{
    public bool Applied => Status == TurnEngineTransitionStatus.Applied;

    /// <summary>
    /// Maps the atomically-shaped turn result back to the framework's standard
    /// action contract, preserving every materialized effect category.
    /// </summary>
    public GameDecision<TState, TResult> ToGameDecision<TResult>(
        TResult acceptedResult,
        Func<TurnEngineRejection, TResult> createRejectedResult)
    {
        ArgumentNullException.ThrowIfNull(createRejectedResult);
        if (Applied)
        {
            return new(
                DecisionStatus.Accepted,
                State,
                acceptedResult,
                Effects.Economy,
                Effects.Quotas,
                Effects.Records,
                Effects.Events,
                Effects.Schedules,
                CustomEffects: Effects.Custom);
        }

        var rejection = Rejection ?? throw new InvalidOperationException(
            "A rejected turn-result transition must contain a rejection.");
        return new(
            DecisionStatus.Rejected,
            State,
            createRejectedResult(rejection),
            [],
            [],
            [],
            [],
            [],
            rejection.Code);
    }
}

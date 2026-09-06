namespace BotFramework.Sdk.Execution;

/// <summary>
/// Atomically-shaped result of applying a <see cref="RoundResult{TState,TPhase}"/>.
/// Rejections return the complete original aggregate state and no effects.
/// </summary>
public sealed record RoundResultTransition<TState, TPhase>(
    RoundEngineTransitionStatus Status,
    TState State,
    RoundEngineState<TPhase> Rounds,
    GameEffectSet Effects,
    RoundCompletion<TPhase>? Completion = null,
    RoundEngineRejection? Rejection = null)
    where TPhase : notnull
{
    public bool Applied => Status == RoundEngineTransitionStatus.Applied;

    /// <summary>Maps this result to the normal atomic game-action contract.</summary>
    public GameDecision<TState, TResult> ToGameDecision<TResult>(
        TResult acceptedResult,
        Func<RoundEngineRejection, TResult> createRejectedResult)
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
            "A rejected round-result transition must contain a rejection.");
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

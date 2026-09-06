namespace BotFramework.Sdk.Execution;

/// <summary>
/// Immutable state machine for games with explicit, game-defined phases and
/// rounds. It does not prescribe a phase graph: a game may transition from any
/// phase to any other phase, then explicitly begin its next logical round.
/// </summary>
public sealed class RoundEngine<TPhase>(IEqualityComparer<TPhase>? comparer = null)
    where TPhase : notnull
{
    private readonly IEqualityComparer<TPhase> _comparer = comparer ?? EqualityComparer<TPhase>.Default;

    /// <summary>Creates an unstarted match and declares its first phase.</summary>
    public RoundEngineState<TPhase> CreateWaiting(TPhase firstPhase)
    {
        ArgumentNullException.ThrowIfNull(firstPhase);
        return new(RoundGameStatus.WaitingToStart, 0, 0, firstPhase, null);
    }

    /// <summary>Starts round one at the phase declared by <see cref="CreateWaiting"/>.</summary>
    public RoundEngineTransition<TPhase> Start(
        RoundEngineState<TPhase> state,
        DateTimeOffset? firstPhaseDeadline = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Status != RoundGameStatus.WaitingToStart)
            return Reject(state, RoundEngineRejectionReason.GameNotWaiting);

        return Accept(new(RoundGameStatus.Active, 1, 1, state.Phase, firstPhaseDeadline));
    }

    /// <summary>Keeps the current phase and deadline after a valid rule result.</summary>
    public RoundEngineTransition<TPhase> Continue(
        RoundEngineState<TPhase> state,
        RoundPhaseToken<TPhase> expectedPhase)
    {
        if (!CanApplyToActivePhase(state, expectedPhase, out var rejection))
            return Reject(state, rejection);

        return Accept(state);
    }

    /// <summary>Enters a game-defined phase within the current round.</summary>
    public RoundEngineTransition<TPhase> AdvanceTo(
        RoundEngineState<TPhase> state,
        RoundPhaseToken<TPhase> expectedPhase,
        TPhase nextPhase,
        DateTimeOffset? nextPhaseDeadline = null)
    {
        ArgumentNullException.ThrowIfNull(nextPhase);
        if (!CanApplyToActivePhase(state, expectedPhase, out var rejection))
            return Reject(state, rejection);

        return Accept(new(
            RoundGameStatus.Active,
            state.Round,
            checked(state.PhaseNumber + 1),
            nextPhase,
            nextPhaseDeadline));
    }

    /// <summary>Starts the next logical round at its game-defined first phase.</summary>
    public RoundEngineTransition<TPhase> NextRound(
        RoundEngineState<TPhase> state,
        RoundPhaseToken<TPhase> expectedPhase,
        TPhase firstPhase,
        DateTimeOffset? firstPhaseDeadline = null)
    {
        ArgumentNullException.ThrowIfNull(firstPhase);
        if (!CanApplyToActivePhase(state, expectedPhase, out var rejection))
            return Reject(state, rejection);

        return Accept(new(
            RoundGameStatus.Active,
            checked(state.Round + 1),
            checked(state.PhaseNumber + 1),
            firstPhase,
            firstPhaseDeadline));
    }

    /// <summary>
    /// Keeps the current phase while waiting for a generic durable input
    /// request. The request expiry becomes the phase deadline.
    /// </summary>
    public RoundEngineTransition<TPhase> WaitFor(
        RoundEngineState<TPhase> state,
        RoundPhaseToken<TPhase> expectedPhase,
        InputRequestEffect input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!CanApplyToActivePhase(state, expectedPhase, out var rejection))
            return Reject(state, rejection);

        return Accept(new(
            RoundGameStatus.Active,
            state.Round,
            state.PhaseNumber,
            state.Phase,
            input.ExpiresAt));
    }

    /// <summary>
    /// Validates that an exact phase deadline is due. It deliberately keeps
    /// phase state unchanged: the game rule chooses whether expiry advances a
    /// phase, starts a new round, completes, or applies a penalty.
    /// </summary>
    public RoundEngineTransition<TPhase> Timeout(
        RoundEngineState<TPhase> state,
        RoundPhaseToken<TPhase> expectedPhase,
        DateTimeOffset utcNow)
    {
        if (!CanApplyToActivePhase(state, expectedPhase, out var rejection))
            return Reject(state, rejection);
        if (state.PhaseDeadline is not { } deadline)
            return Reject(state, RoundEngineRejectionReason.DeadlineNotSet);
        if (utcNow < deadline)
            return Reject(state, RoundEngineRejectionReason.PhaseNotExpired);

        return Accept(state);
    }

    /// <summary>Pauses the current phase and clears its active deadline.</summary>
    public RoundEngineTransition<TPhase> Suspend(
        RoundEngineState<TPhase> state,
        RoundPhaseToken<TPhase> expectedPhase)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(expectedPhase);
        if (state.Status != RoundGameStatus.Active)
            return Reject(state, RoundEngineRejectionReason.GameNotActive);
        if (!Matches(state, expectedPhase))
            return Reject(state, RoundEngineRejectionReason.StalePhase);

        return Accept(new(
            RoundGameStatus.Suspended,
            state.Round,
            state.PhaseNumber,
            state.Phase,
            null));
    }

    /// <summary>Restores a suspended phase with an optional fresh deadline.</summary>
    public RoundEngineTransition<TPhase> Resume(
        RoundEngineState<TPhase> state,
        RoundPhaseToken<TPhase> expectedPhase,
        DateTimeOffset? phaseDeadline = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(expectedPhase);
        if (state.Status != RoundGameStatus.Suspended)
            return Reject(state, RoundEngineRejectionReason.GameNotSuspended);
        if (!Matches(state, expectedPhase))
            return Reject(state, RoundEngineRejectionReason.StalePhase);

        return Accept(new(
            RoundGameStatus.Active,
            state.Round,
            state.PhaseNumber,
            state.Phase,
            phaseDeadline));
    }

    /// <summary>Marks a nonterminal game complete and clears its deadline.</summary>
    public RoundEngineTransition<TPhase> Complete(RoundEngineState<TPhase> state) =>
        End(state, RoundGameStatus.Completed);

    /// <summary>Completes the active phase only when its token still matches.</summary>
    public RoundEngineTransition<TPhase> Complete(
        RoundEngineState<TPhase> state,
        RoundPhaseToken<TPhase> expectedPhase)
    {
        if (!CanApplyToActivePhase(state, expectedPhase, out var rejection))
            return Reject(state, rejection);
        return End(state, RoundGameStatus.Completed);
    }

    /// <summary>Aborts a nonterminal game without prescribing compensation.</summary>
    public RoundEngineTransition<TPhase> Abort(RoundEngineState<TPhase> state) =>
        End(state, RoundGameStatus.Aborted);

    /// <summary>
    /// Applies a phase rule after token validation. A stale or rejected result
    /// returns the original game state and an empty effect set.
    /// </summary>
    public RoundResultTransition<TState, TPhase> Apply<TState>(
        TState currentState,
        RoundEngineState<TPhase> rounds,
        RoundPhaseToken<TPhase> expectedPhase,
        RoundResult<TState, TPhase> result)
    {
        ArgumentNullException.ThrowIfNull(currentState);
        ArgumentNullException.ThrowIfNull(rounds);
        ArgumentNullException.ThrowIfNull(expectedPhase);
        ArgumentNullException.ThrowIfNull(result);

        var transition = result.Completion switch
        {
            RoundCompletion<TPhase>.ContinuePhase => Continue(rounds, expectedPhase),
            RoundCompletion<TPhase>.CompleteGame => Complete(rounds, expectedPhase),
            RoundCompletion<TPhase>.AdvancePhase advance => AdvanceTo(
                rounds,
                expectedPhase,
                advance.NextPhase,
                advance.NextPhaseDeadline),
            RoundCompletion<TPhase>.StartNextRound nextRound => NextRound(
                rounds,
                expectedPhase,
                nextRound.FirstPhase,
                nextRound.FirstPhaseDeadline),
            RoundCompletion<TPhase>.WaitForInput wait => WaitFor(rounds, expectedPhase, wait.Input),
            _ => throw new InvalidOperationException(
                $"Unknown round completion '{result.Completion.GetType().Name}'."),
        };

        return transition.Applied
            ? new(
                RoundEngineTransitionStatus.Applied,
                result.State,
                transition.State,
                result.Effects,
                result.Completion)
            : RejectedResult<TState>(currentState, rounds, transition.Rejection);
    }

    /// <summary>
    /// Applies a result and stores its round snapshot in an aggregate. The
    /// binder is not invoked on rejection, preserving the complete aggregate.
    /// </summary>
    public RoundResultTransition<TState, TPhase> Apply<TState>(
        TState currentState,
        RoundEngineState<TPhase> rounds,
        RoundPhaseToken<TPhase> expectedPhase,
        RoundResult<TState, TPhase> result,
        Func<TState, RoundEngineState<TPhase>, TState> bindRounds)
    {
        ArgumentNullException.ThrowIfNull(bindRounds);

        var transition = Apply(currentState, rounds, expectedPhase, result);
        if (!transition.Applied)
            return transition;

        var state = bindRounds(transition.State, transition.Rounds);
        if (state is null)
            throw new InvalidOperationException("The round-state binder cannot return null.");
        return transition with { State = state };
    }

    /// <summary>
    /// Validates a due deadline, then applies a phase rule. The provided rule
    /// remains responsible for choosing the phase progression after timeout.
    /// </summary>
    public RoundResultTransition<TState, TPhase> ApplyTimeout<TState>(
        TState currentState,
        RoundEngineState<TPhase> rounds,
        RoundPhaseToken<TPhase> expectedPhase,
        DateTimeOffset utcNow,
        RoundResult<TState, TPhase> result)
    {
        ArgumentNullException.ThrowIfNull(currentState);
        ArgumentNullException.ThrowIfNull(rounds);
        ArgumentNullException.ThrowIfNull(expectedPhase);
        ArgumentNullException.ThrowIfNull(result);

        var timeout = Timeout(rounds, expectedPhase, utcNow);
        return timeout.Applied
            ? Apply(currentState, rounds, expectedPhase, result)
            : RejectedResult<TState>(currentState, rounds, timeout.Rejection);
    }

    /// <summary>
    /// Validates a timeout, applies a result and stores its round snapshot in
    /// an aggregate. The binder is skipped for stale or early deadlines.
    /// </summary>
    public RoundResultTransition<TState, TPhase> ApplyTimeout<TState>(
        TState currentState,
        RoundEngineState<TPhase> rounds,
        RoundPhaseToken<TPhase> expectedPhase,
        DateTimeOffset utcNow,
        RoundResult<TState, TPhase> result,
        Func<TState, RoundEngineState<TPhase>, TState> bindRounds)
    {
        ArgumentNullException.ThrowIfNull(bindRounds);

        var transition = ApplyTimeout(currentState, rounds, expectedPhase, utcNow, result);
        if (!transition.Applied)
            return transition;

        var state = bindRounds(transition.State, transition.Rounds);
        if (state is null)
            throw new InvalidOperationException("The round-state binder cannot return null.");
        return transition with { State = state };
    }

    private static RoundResultTransition<TState, TPhase> RejectedResult<TState>(
        TState currentState,
        RoundEngineState<TPhase> rounds,
        RoundEngineRejection? rejection) =>
        new(
            RoundEngineTransitionStatus.Rejected,
            currentState,
            rounds,
            GameEffectSet.Empty,
            Rejection: rejection ?? throw new InvalidOperationException(
                "A rejected round transition must contain a rejection."));

    private static RoundEngineTransition<TPhase> End(
        RoundEngineState<TPhase> state,
        RoundGameStatus status)
    {
        ArgumentNullException.ThrowIfNull(state);
        return IsTerminal(state)
            ? Reject(state, RoundEngineRejectionReason.GameIsTerminal)
            : Accept(new(status, state.Round, state.PhaseNumber, state.Phase, null));
    }

    private bool CanApplyToActivePhase(
        RoundEngineState<TPhase>? state,
        RoundPhaseToken<TPhase>? expectedPhase,
        out RoundEngineRejectionReason rejection)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(expectedPhase);
        if (state.Status != RoundGameStatus.Active)
        {
            rejection = state.Status is RoundGameStatus.Completed or RoundGameStatus.Aborted
                ? RoundEngineRejectionReason.GameIsTerminal
                : RoundEngineRejectionReason.GameNotActive;
            return false;
        }
        if (!Matches(state, expectedPhase))
        {
            rejection = RoundEngineRejectionReason.StalePhase;
            return false;
        }

        rejection = default;
        return true;
    }

    private bool Matches(RoundEngineState<TPhase> state, RoundPhaseToken<TPhase> token) =>
        state.Round == token.Round
        && state.PhaseNumber == token.PhaseNumber
        && _comparer.Equals(state.Phase, token.Phase);

    private static bool IsTerminal(RoundEngineState<TPhase> state) =>
        state.Status is RoundGameStatus.Completed or RoundGameStatus.Aborted;

    private static RoundEngineTransition<TPhase> Accept(RoundEngineState<TPhase> state) =>
        new(RoundEngineTransitionStatus.Applied, state);

    private static RoundEngineTransition<TPhase> Reject(
        RoundEngineState<TPhase>? state,
        RoundEngineRejectionReason reason)
    {
        ArgumentNullException.ThrowIfNull(state);
        return new(RoundEngineTransitionStatus.Rejected, state, new RoundEngineRejection(reason));
    }
}

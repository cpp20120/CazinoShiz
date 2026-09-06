namespace BotFramework.Sdk.Execution;

/// <summary>
/// Immutable state machine for ordinary cyclic turns. It owns participant order,
/// current turn identity and deadlines, while a game continues to own move
/// rules, scoring, rendering and terminal outcome semantics.
/// </summary>
public sealed class TurnEngine<TPlayerId>(IEqualityComparer<TPlayerId>? comparer = null)
    where TPlayerId : notnull
{
    private readonly IEqualityComparer<TPlayerId> _comparer = comparer ?? EqualityComparer<TPlayerId>.Default;

    public TurnEngineState<TPlayerId> CreateWaiting(IEnumerable<TPlayerId> playerOrder)
    {
        ArgumentNullException.ThrowIfNull(playerOrder);
        var players = playerOrder.ToArray();
        EnsureUnique(players);
        return new(TurnGameStatus.WaitingForPlayers, players, -1, 0, 0, null);
    }

    /// <summary>Admits a player before the first turn. Mid-game join policy stays game-specific.</summary>
    public TurnEngineTransition<TPlayerId> AddPlayer(TurnEngineState<TPlayerId> state, TPlayerId playerId)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Status != TurnGameStatus.WaitingForPlayers)
            return Reject(state, TurnEngineRejectionReason.GameNotWaiting);
        if (Contains(state.PlayerOrder, playerId))
            return Reject(state, TurnEngineRejectionReason.PlayerAlreadyJoined);

        return Accept(new(TurnGameStatus.WaitingForPlayers, [.. state.PlayerOrder, playerId], -1, 0, 0, null));
    }

    /// <summary>Starts the first player in the declared order.</summary>
    public TurnEngineTransition<TPlayerId> Start(
        TurnEngineState<TPlayerId> state,
        DateTimeOffset? turnDeadline = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Status != TurnGameStatus.WaitingForPlayers)
            return Reject(state, TurnEngineRejectionReason.GameNotWaiting);
        if (state.PlayerOrder.Count == 0)
            return Reject(state, TurnEngineRejectionReason.NoPlayers);

        return StartAt(state, state.PlayerOrder[0], turnDeadline);
    }

    /// <summary>Starts a selected admitted player, preserving the declared cyclic order.</summary>
    public TurnEngineTransition<TPlayerId> StartAt(
        TurnEngineState<TPlayerId> state,
        TPlayerId playerId,
        DateTimeOffset? turnDeadline = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Status != TurnGameStatus.WaitingForPlayers)
            return Reject(state, TurnEngineRejectionReason.GameNotWaiting);
        if (state.PlayerOrder.Count == 0)
            return Reject(state, TurnEngineRejectionReason.NoPlayers);

        var index = IndexOf(state.PlayerOrder, playerId);
        if (index < 0)
            return Reject(state, TurnEngineRejectionReason.PlayerNotFound);

        return Accept(new(TurnGameStatus.Active, state.PlayerOrder, index, 1, 1, turnDeadline));
    }

    /// <summary>Moves to the next player only when the supplied token matches the active turn.</summary>
    public TurnEngineTransition<TPlayerId> Advance(
        TurnEngineState<TPlayerId> state,
        TurnToken<TPlayerId> expectedTurn,
        DateTimeOffset? nextTurnDeadline = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(expectedTurn);
        if (state.Status != TurnGameStatus.Active)
            return Reject(state, TurnEngineRejectionReason.GameNotActive);
        if (!Matches(state, expectedTurn))
            return Reject(state, TurnEngineRejectionReason.StaleTurn);

        return Accept(AdvanceFromActive(state, nextTurnDeadline));
    }

    /// <summary>
    /// Keeps the current player and deadline after a valid rule result. This is
    /// useful for games where one player can make several consecutive moves.
    /// </summary>
    public TurnEngineTransition<TPlayerId> Continue(
        TurnEngineState<TPlayerId> state,
        TurnToken<TPlayerId> expectedTurn)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(expectedTurn);
        if (state.Status != TurnGameStatus.Active)
            return Reject(state, TurnEngineRejectionReason.GameNotActive);
        if (!Matches(state, expectedTurn))
            return Reject(state, TurnEngineRejectionReason.StaleTurn);

        return Accept(new(
            TurnGameStatus.Active,
            state.PlayerOrder,
            state.CurrentPlayerIndex,
            state.TurnNumber,
            state.Round,
            state.TurnDeadline));
    }

    /// <summary>
    /// Assigns the next turn to an admitted player. The turn number always
    /// advances; the round advances only when the selected player is earlier in
    /// the declared cyclic order than the current player.
    /// </summary>
    public TurnEngineTransition<TPlayerId> PassTo(
        TurnEngineState<TPlayerId> state,
        TurnToken<TPlayerId> expectedTurn,
        TPlayerId playerId,
        DateTimeOffset? nextTurnDeadline = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(expectedTurn);
        ArgumentNullException.ThrowIfNull(playerId);
        if (state.Status != TurnGameStatus.Active)
            return Reject(state, TurnEngineRejectionReason.GameNotActive);
        if (!Matches(state, expectedTurn))
            return Reject(state, TurnEngineRejectionReason.StaleTurn);

        var nextIndex = IndexOf(state.PlayerOrder, playerId);
        if (nextIndex < 0)
            return Reject(state, TurnEngineRejectionReason.PlayerNotFound);

        var wraps = nextIndex < state.CurrentPlayerIndex;
        return Accept(new(
            TurnGameStatus.Active,
            state.PlayerOrder,
            nextIndex,
            checked(state.TurnNumber + 1),
            wraps ? checked(state.Round + 1) : state.Round,
            nextTurnDeadline));
    }

    /// <summary>
    /// Keeps the current player while it waits for a generic, durable input
    /// request. The input expiry becomes the turn deadline so the normal
    /// token-safe timeout path can resolve an unanswered request.
    /// </summary>
    public TurnEngineTransition<TPlayerId> WaitFor(
        TurnEngineState<TPlayerId> state,
        TurnToken<TPlayerId> expectedTurn,
        InputRequestEffect input)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(expectedTurn);
        ArgumentNullException.ThrowIfNull(input);
        if (state.Status != TurnGameStatus.Active)
            return Reject(state, TurnEngineRejectionReason.GameNotActive);
        if (!Matches(state, expectedTurn))
            return Reject(state, TurnEngineRejectionReason.StaleTurn);

        return Accept(new(
            TurnGameStatus.Active,
            state.PlayerOrder,
            state.CurrentPlayerIndex,
            state.TurnNumber,
            state.Round,
            input.ExpiresAt));
    }

    /// <summary>Advances a due turn. An early or stale timeout leaves state untouched.</summary>
    public TurnEngineTransition<TPlayerId> Timeout(
        TurnEngineState<TPlayerId> state,
        TurnToken<TPlayerId> expectedTurn,
        DateTimeOffset utcNow,
        DateTimeOffset? nextTurnDeadline = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(expectedTurn);
        if (state.Status != TurnGameStatus.Active)
            return Reject(state, TurnEngineRejectionReason.GameNotActive);
        if (!Matches(state, expectedTurn))
            return Reject(state, TurnEngineRejectionReason.StaleTurn);
        if (state.TurnDeadline is not { } deadline)
            return Reject(state, TurnEngineRejectionReason.DeadlineNotSet);
        if (utcNow < deadline)
            return Reject(state, TurnEngineRejectionReason.TurnNotExpired);

        return Accept(AdvanceFromActive(state, nextTurnDeadline));
    }

    /// <summary>Pauses the current turn and clears its deadline.</summary>
    public TurnEngineTransition<TPlayerId> Suspend(
        TurnEngineState<TPlayerId> state,
        TurnToken<TPlayerId> expectedTurn)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(expectedTurn);
        if (state.Status != TurnGameStatus.Active)
            return Reject(state, TurnEngineRejectionReason.GameNotActive);
        if (!Matches(state, expectedTurn))
            return Reject(state, TurnEngineRejectionReason.StaleTurn);

        return Accept(new(
            TurnGameStatus.Suspended,
            state.PlayerOrder,
            state.CurrentPlayerIndex,
            state.TurnNumber,
            state.Round,
            null));
    }

    /// <summary>Restores the same turn with a fresh optional deadline.</summary>
    public TurnEngineTransition<TPlayerId> Resume(
        TurnEngineState<TPlayerId> state,
        TurnToken<TPlayerId> expectedTurn,
        DateTimeOffset? turnDeadline = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(expectedTurn);
        if (state.Status != TurnGameStatus.Suspended)
            return Reject(state, TurnEngineRejectionReason.GameNotSuspended);
        if (!Matches(state, expectedTurn))
            return Reject(state, TurnEngineRejectionReason.StaleTurn);

        return Accept(new(
            TurnGameStatus.Active,
            state.PlayerOrder,
            state.CurrentPlayerIndex,
            state.TurnNumber,
            state.Round,
            turnDeadline));
    }

    /// <summary>Removes an admitted player and rotates if that player owned the current turn.</summary>
    public TurnEngineTransition<TPlayerId> RemovePlayer(
        TurnEngineState<TPlayerId> state,
        TPlayerId playerId,
        DateTimeOffset? nextTurnDeadline = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (IsTerminal(state))
            return Reject(state, TurnEngineRejectionReason.GameIsTerminal);

        var removedIndex = IndexOf(state.PlayerOrder, playerId);
        if (removedIndex < 0)
            return Reject(state, TurnEngineRejectionReason.PlayerNotFound);

        var players = state.PlayerOrder.Where((_, index) => index != removedIndex).ToArray();
        if (state.Status == TurnGameStatus.WaitingForPlayers)
            return Accept(new(TurnGameStatus.WaitingForPlayers, players, -1, 0, 0, null));
        if (players.Length == 0)
            return Accept(new(TurnGameStatus.Aborted, players, -1, state.TurnNumber, state.Round, null));

        return removedIndex == state.CurrentPlayerIndex
            ? Accept(AdvanceAfterCurrentPlayerLeaves(state, players, nextTurnDeadline))
            : Accept(RemoveWaitingPlayer(state, players, removedIndex));
    }

    /// <summary>Marks a nonterminal game complete; game rules decide the winner and result separately.</summary>
    public TurnEngineTransition<TPlayerId> Complete(TurnEngineState<TPlayerId> state) =>
        End(state, TurnGameStatus.Completed);

    /// <summary>Completes the active turn only when the supplied token matches.</summary>
    public TurnEngineTransition<TPlayerId> Complete(
        TurnEngineState<TPlayerId> state,
        TurnToken<TPlayerId> expectedTurn)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(expectedTurn);
        if (state.Status != TurnGameStatus.Active)
            return Reject(state, TurnEngineRejectionReason.GameNotActive);
        if (!Matches(state, expectedTurn))
            return Reject(state, TurnEngineRejectionReason.StaleTurn);

        return End(state, TurnGameStatus.Completed);
    }

    /// <summary>Marks a nonterminal game aborted without prescribing compensation policy.</summary>
    public TurnEngineTransition<TPlayerId> Abort(TurnEngineState<TPlayerId> state) =>
        End(state, TurnGameStatus.Aborted);

    /// <summary>
    /// Applies a game rule result to the FSM. On a stale or otherwise rejected
    /// transition it returns the supplied current state and an empty effect set.
    /// Callers can therefore persist this value directly without separately
    /// remembering to discard rule output after a token check fails.
    /// </summary>
    public TurnResultTransition<TState, TPlayerId> Apply<TState>(
        TState currentState,
        TurnEngineState<TPlayerId> turns,
        TurnToken<TPlayerId> expectedTurn,
        TurnResult<TState, TPlayerId> result)
    {
        ArgumentNullException.ThrowIfNull(currentState);
        ArgumentNullException.ThrowIfNull(turns);
        ArgumentNullException.ThrowIfNull(expectedTurn);
        ArgumentNullException.ThrowIfNull(result);

        var transition = result.Completion switch
        {
            TurnCompletion<TPlayerId>.ContinueTurn => Continue(turns, expectedTurn),
            TurnCompletion<TPlayerId>.CompleteTurn => Complete(turns, expectedTurn),
            TurnCompletion<TPlayerId>.PassToTurn pass => PassTo(
                turns,
                expectedTurn,
                pass.PlayerId,
                pass.NextTurnDeadline),
            TurnCompletion<TPlayerId>.WaitForInputTurn wait => WaitFor(turns, expectedTurn, wait.Input),
            _ => throw new InvalidOperationException(
                $"Unknown turn completion '{result.Completion.GetType().Name}'."),
        };

        return transition.Applied
            ? new(
                TurnEngineTransitionStatus.Applied,
                result.State,
                transition.State,
                result.Effects,
                result.Completion)
            : new(
                TurnEngineTransitionStatus.Rejected,
                currentState,
                turns,
                GameEffectSet.Empty,
                Rejection: transition.Rejection);
    }

    /// <summary>
    /// Applies a turn result and binds the resulting turn snapshot back into a
    /// game aggregate that embeds it. The binder is not invoked on rejection,
    /// so a stale input leaves the complete aggregate untouched.
    /// </summary>
    public TurnResultTransition<TState, TPlayerId> Apply<TState>(
        TState currentState,
        TurnEngineState<TPlayerId> turns,
        TurnToken<TPlayerId> expectedTurn,
        TurnResult<TState, TPlayerId> result,
        Func<TState, TurnEngineState<TPlayerId>, TState> bindTurns)
    {
        ArgumentNullException.ThrowIfNull(bindTurns);

        var transition = Apply(currentState, turns, expectedTurn, result);
        if (!transition.Applied)
            return transition;

        var state = bindTurns(transition.State, transition.Turns);
        if (state is null)
            throw new InvalidOperationException("The turn-state binder cannot return null.");
        return transition with { State = state };
    }

    private static TurnEngineTransition<TPlayerId> End(TurnEngineState<TPlayerId> state, TurnGameStatus status)
    {
        ArgumentNullException.ThrowIfNull(state);
        return IsTerminal(state)
            ? Reject(state, TurnEngineRejectionReason.GameIsTerminal)
            : Accept(new(status, state.PlayerOrder, -1, state.TurnNumber, state.Round, null));
    }

    private static TurnEngineState<TPlayerId> AdvanceFromActive(
        TurnEngineState<TPlayerId> state,
        DateTimeOffset? nextTurnDeadline)
    {
        var wraps = state.CurrentPlayerIndex == state.PlayerOrder.Count - 1;
        var nextIndex = wraps ? 0 : state.CurrentPlayerIndex + 1;
        return new(
            TurnGameStatus.Active,
            state.PlayerOrder,
            nextIndex,
            checked(state.TurnNumber + 1),
            wraps ? checked(state.Round + 1) : state.Round,
            nextTurnDeadline);
    }

    private static TurnEngineState<TPlayerId> AdvanceAfterCurrentPlayerLeaves(
        TurnEngineState<TPlayerId> state,
        TPlayerId[] players,
        DateTimeOffset? nextTurnDeadline)
    {
        var wraps = state.CurrentPlayerIndex == state.PlayerOrder.Count - 1;
        var nextIndex = wraps ? 0 : state.CurrentPlayerIndex;
        var status = state.Status == TurnGameStatus.Active ? TurnGameStatus.Active : TurnGameStatus.Suspended;
        return new(
            status,
            players,
            nextIndex,
            checked(state.TurnNumber + 1),
            wraps ? checked(state.Round + 1) : state.Round,
            status == TurnGameStatus.Active ? nextTurnDeadline : null);
    }

    private static TurnEngineState<TPlayerId> RemoveWaitingPlayer(
        TurnEngineState<TPlayerId> state,
        TPlayerId[] players,
        int removedIndex)
    {
        var currentIndex = removedIndex < state.CurrentPlayerIndex
            ? state.CurrentPlayerIndex - 1
            : state.CurrentPlayerIndex;
        return new(
            state.Status,
            players,
            currentIndex,
            state.TurnNumber,
            state.Round,
            state.Status == TurnGameStatus.Active ? state.TurnDeadline : null);
    }

    private bool Contains(IReadOnlyList<TPlayerId> players, TPlayerId playerId) =>
        IndexOf(players, playerId) >= 0;

    private int IndexOf(IReadOnlyList<TPlayerId> players, TPlayerId playerId)
    {
        for (var index = 0; index < players.Count; index++)
        {
            if (_comparer.Equals(players[index], playerId))
                return index;
        }

        return -1;
    }

    private bool Matches(TurnEngineState<TPlayerId> state, TurnToken<TPlayerId> token) =>
        state.TurnNumber == token.Number && _comparer.Equals(state.CurrentPlayerId, token.PlayerId);

    private void EnsureUnique(TPlayerId[] players)
    {
        for (var index = 0; index < players.Length; index++)
        {
            for (var other = index + 1; other < players.Length; other++)
            {
                if (_comparer.Equals(players[index], players[other]))
                    throw new ArgumentException("Player order cannot contain duplicate player ids.", nameof(players));
            }
        }
    }

    private static bool IsTerminal(TurnEngineState<TPlayerId> state) =>
        state.Status is TurnGameStatus.Completed or TurnGameStatus.Aborted;

    private static TurnEngineTransition<TPlayerId> Accept(TurnEngineState<TPlayerId> state) =>
        new(TurnEngineTransitionStatus.Applied, state);

    private static TurnEngineTransition<TPlayerId> Reject(
        TurnEngineState<TPlayerId> state,
        TurnEngineRejectionReason reason) =>
        new(TurnEngineTransitionStatus.Rejected, state, new TurnEngineRejection(reason));
}

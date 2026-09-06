namespace BotFramework.Sdk.Execution;

/// <summary>
/// Pure FIFO matcher for one exact game-defined compatibility key. It neither
/// polls nor creates networked rooms: a service persists its state, invokes
/// <see cref="TryFormMatch"/>, then creates a regular <see cref="LobbyState{TPlayerId}"/>.
/// </summary>
public sealed class MatchmakingEngine<TPlayerId, TMatchKey>(
    IEqualityComparer<TPlayerId>? playerComparer = null,
    IEqualityComparer<TMatchKey>? matchKeyComparer = null)
    where TPlayerId : notnull
    where TMatchKey : notnull
{
    private readonly IEqualityComparer<TPlayerId> _players = playerComparer ?? EqualityComparer<TPlayerId>.Default;
    private readonly IEqualityComparer<TMatchKey> _keys = matchKeyComparer ?? EqualityComparer<TMatchKey>.Default;

    public MatchmakingState<TPlayerId, TMatchKey> Create() => new([]);

    public MatchmakingTransition<TPlayerId, TMatchKey> Enqueue(
        MatchmakingState<TPlayerId, TMatchKey> state,
        TPlayerId playerId,
        TMatchKey matchKey,
        DateTimeOffset enqueuedAt)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(playerId);
        ArgumentNullException.ThrowIfNull(matchKey);
        if (state.Tickets.Any(ticket => _players.Equals(ticket.PlayerId, playerId)))
            return Reject(state, MatchmakingRejectionReason.PlayerAlreadyQueued);
        if (state.Tickets.Count != 0 && enqueuedAt < state.Tickets[^1].EnqueuedAt)
            throw new ArgumentOutOfRangeException(nameof(enqueuedAt), "Tickets must be enqueued in chronological order.");

        return Accept(new([.. state.Tickets, new MatchmakingTicket<TPlayerId, TMatchKey>(playerId, matchKey, enqueuedAt)]));
    }

    public MatchmakingTransition<TPlayerId, TMatchKey> Dequeue(
        MatchmakingState<TPlayerId, TMatchKey> state,
        TPlayerId playerId)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(playerId);
        if (!state.Tickets.Any(ticket => _players.Equals(ticket.PlayerId, playerId)))
            return Reject(state, MatchmakingRejectionReason.PlayerNotQueued);

        return Accept(new(state.Tickets.Where(ticket => !_players.Equals(ticket.PlayerId, playerId))));
    }

    /// <summary>
    /// Selects the earliest compatibility bucket containing enough players. A
    /// game owns wider rating-window and bot-fill policy; this primitive only
    /// guarantees deterministic FIFO selection for an exact key.
    /// </summary>
    public MatchmakingTransition<TPlayerId, TMatchKey> TryFormMatch(
        MatchmakingState<TPlayerId, TMatchKey> state,
        int minimumPlayers,
        int maximumPlayers)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentOutOfRangeException.ThrowIfLessThan(minimumPlayers, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumPlayers, minimumPlayers);

        foreach (var matchKey in state.Tickets.Select(static ticket => ticket.MatchKey))
        {
            var compatible = state.Tickets
                .Where(ticket => _keys.Equals(ticket.MatchKey, matchKey))
                .Take(maximumPlayers)
                .ToArray();
            if (compatible.Length < minimumPlayers)
                continue;

            var players = compatible.Select(ticket => ticket.PlayerId).ToArray();
            var match = new MatchmakingMatch<TPlayerId, TMatchKey>(matchKey, players);
            return new(
                MatchmakingTransitionStatus.Applied,
                new(state.Tickets.Where(ticket => !players.Any(player => _players.Equals(player, ticket.PlayerId)))),
                match);
        }

        return new(MatchmakingTransitionStatus.NoMatch, state);
    }

    private static MatchmakingTransition<TPlayerId, TMatchKey> Accept(
        MatchmakingState<TPlayerId, TMatchKey> state) =>
        new(MatchmakingTransitionStatus.Applied, state);

    private static MatchmakingTransition<TPlayerId, TMatchKey> Reject(
        MatchmakingState<TPlayerId, TMatchKey> state,
        MatchmakingRejectionReason reason) =>
        new(MatchmakingTransitionStatus.Rejected, state, Rejection: new MatchmakingRejection(reason));
}

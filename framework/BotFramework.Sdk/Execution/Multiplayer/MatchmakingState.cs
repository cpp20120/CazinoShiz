using System.Collections.ObjectModel;

namespace BotFramework.Sdk.Execution;

/// <summary>
/// Serializable, FIFO matchmaking queue. The game owns the meaning of its
/// compatibility key (mode, region, rating band, privacy code and so on).
/// </summary>
public sealed record MatchmakingState<TPlayerId, TMatchKey>
    where TPlayerId : notnull
    where TMatchKey : notnull
{
    public MatchmakingState(IEnumerable<MatchmakingTicket<TPlayerId, TMatchKey>> tickets)
    {
        ArgumentNullException.ThrowIfNull(tickets);
        var copy = tickets.ToArray();
        if (copy.Any(static ticket => ticket is null))
            throw new ArgumentException("Matchmaking tickets cannot contain null.", nameof(tickets));
        if (copy.Select(ticket => ticket.PlayerId).Distinct().Count() != copy.Length)
            throw new ArgumentException("A player can have only one matchmaking ticket.", nameof(tickets));
        if (copy.Zip(copy.Skip(1)).Any(pair => pair.First.EnqueuedAt > pair.Second.EnqueuedAt))
            throw new ArgumentException("Matchmaking tickets must be ordered by enqueue time.", nameof(tickets));

        Tickets = new ReadOnlyCollection<MatchmakingTicket<TPlayerId, TMatchKey>>(copy);
    }

    public IReadOnlyList<MatchmakingTicket<TPlayerId, TMatchKey>> Tickets { get; }
}

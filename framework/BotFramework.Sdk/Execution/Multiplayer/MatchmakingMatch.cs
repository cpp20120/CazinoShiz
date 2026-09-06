using System.Collections.ObjectModel;

namespace BotFramework.Sdk.Execution;

/// <summary>Compatible players selected from a queue; the game turns them into a lobby or match.</summary>
public sealed record MatchmakingMatch<TPlayerId, TMatchKey>
    where TPlayerId : notnull
    where TMatchKey : notnull
{
    public MatchmakingMatch(TMatchKey matchKey, IEnumerable<TPlayerId> players)
    {
        ArgumentNullException.ThrowIfNull(matchKey);
        ArgumentNullException.ThrowIfNull(players);
        var copy = players.ToArray();
        if (copy.Length == 0)
            throw new ArgumentException("A match requires at least one player.", nameof(players));
        if (copy.Distinct().Count() != copy.Length)
            throw new ArgumentException("A match cannot contain a player twice.", nameof(players));

        MatchKey = matchKey;
        Players = new ReadOnlyCollection<TPlayerId>(copy);
    }

    public TMatchKey MatchKey { get; }

    public IReadOnlyList<TPlayerId> Players { get; }
}

using System.Collections.ObjectModel;

namespace BotFramework.Sdk.Execution;

/// <summary>
/// Immutable record of entropy consumed by one game-rule evaluation. Persist it
/// in a game record or replay journal when a game needs outcome-level audit.
/// </summary>
public sealed record GameRandomTrace
{
    public GameRandomTrace(IEnumerable<GameRandomDraw> draws)
    {
        ArgumentNullException.ThrowIfNull(draws);
        var copy = draws.ToArray();
        if (copy.Any(static draw => draw is null))
            throw new ArgumentException("A random trace cannot contain null draws.", nameof(draws));
        if (copy.Select(draw => draw.Name).Distinct(StringComparer.Ordinal).Count() != copy.Length)
            throw new ArgumentException("A random trace cannot consume the same entropy name twice.", nameof(draws));

        Draws = new ReadOnlyCollection<GameRandomDraw>(copy);
    }

    public static GameRandomTrace Empty { get; } = new([]);

    public IReadOnlyList<GameRandomDraw> Draws { get; }
}

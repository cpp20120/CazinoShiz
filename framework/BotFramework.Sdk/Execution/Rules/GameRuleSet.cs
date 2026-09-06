using System.Collections.ObjectModel;

namespace BotFramework.Sdk.Execution;

/// <summary>
/// Ordered set of pure guards. The first failure is returned, giving every
/// command a deterministic and locally renderable rejection reason.
/// </summary>
public sealed class GameRuleSet<TContext>
{
    private readonly IReadOnlyList<GameRule<TContext>> _rules;

    public GameRuleSet(IEnumerable<GameRule<TContext>> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var copy = rules.ToArray();
        if (copy.Any(static rule => rule is null))
            throw new ArgumentException("A rule set cannot contain null rules.", nameof(rules));
        if (copy.Select(rule => rule.Name).Distinct(StringComparer.Ordinal).Count() != copy.Length)
            throw new ArgumentException("Rule names must be unique in a rule set.", nameof(rules));
        _rules = new ReadOnlyCollection<GameRule<TContext>>(copy);
    }

    public IReadOnlyList<GameRule<TContext>> Rules => _rules;

    public GameRuleCheck Evaluate(TContext context)
    {
        foreach (var rule in _rules)
        {
            var result = rule.Evaluate(context);
            if (!result.Allowed)
                return result;
        }
        return GameRuleCheck.Pass;
    }
}

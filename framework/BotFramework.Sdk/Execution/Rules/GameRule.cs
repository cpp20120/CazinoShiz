namespace BotFramework.Sdk.Execution;

/// <summary>
/// Named pure guard that accepts or rejects a game command context. Rules are
/// evaluated in a declared order by <see cref="GameRuleSet{TContext}"/>.
/// </summary>
public sealed class GameRule<TContext>
{
    private readonly Func<TContext, bool> _allows;

    public GameRule(string name, Func<TContext, bool> allows, GameRuleRejection rejection)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A rule name is required.", nameof(name));
        ArgumentNullException.ThrowIfNull(allows);
        ArgumentNullException.ThrowIfNull(rejection);

        Name = name;
        _allows = allows;
        Rejection = rejection;
    }

    public string Name { get; }

    public GameRuleRejection Rejection { get; }

    public GameRuleCheck Evaluate(TContext context) => _allows(context)
        ? GameRuleCheck.Pass
        : GameRuleCheck.Reject(Rejection);
}

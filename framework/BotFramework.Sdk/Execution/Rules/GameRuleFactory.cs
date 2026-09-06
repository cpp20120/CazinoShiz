namespace BotFramework.Sdk.Execution;

#pragma warning disable MA0048 // GameRule<TContext> occupies the conventional file name.

/// <summary>Factories for common declarative game-rule guards.</summary>
public static class GameRule
{
    public static GameRule<TContext> Require<TContext>(
        string name,
        Func<TContext, bool> allows,
        GameRuleRejection rejection) => new(name, allows, rejection);

    public static GameRuleCheck Require(bool condition, GameRuleRejection rejection) =>
        condition ? GameRuleCheck.Pass : GameRuleCheck.Reject(rejection);
}

#pragma warning restore MA0048

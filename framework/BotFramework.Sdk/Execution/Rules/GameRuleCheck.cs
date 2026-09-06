namespace BotFramework.Sdk.Execution;

/// <summary>Result of one declarative game-rule evaluation.</summary>
public sealed record GameRuleCheck(GameRuleRejection? Rejection = null)
{
    public bool Allowed => Rejection is null;

    public static GameRuleCheck Pass { get; } = new();

    public static GameRuleCheck Reject(GameRuleRejection rejection)
    {
        ArgumentNullException.ThrowIfNull(rejection);
        return new(rejection);
    }
}

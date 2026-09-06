using BotFramework.Sdk.Execution;

namespace BotFramework.Host.Execution;

/// <summary>
/// Reusable game descriptor policy for a per-player daily quota. The quota policy
/// supplies all transport- and deployment-specific behavior.
/// </summary>
public abstract class DailyGameQuotaExecutionDescriptor<TCommand, TState, TResult>(
    GameDefinition game,
    IGameDailyQuotaPolicy dailyQuotaPolicy)
    : PlayerGameExecutionDescriptor<TCommand, TState, TResult>(game)
    where TCommand : IPlayerGameCommand
{
    public override IReadOnlyList<QuotaIdentity> Quotas(TCommand command, DateTimeOffset utcNow)
    {
        var quotaId = DailyQuotaId(command);
        if (quotaId is null)
            return [];
        if (string.IsNullOrWhiteSpace(quotaId))
            throw new InvalidOperationException("A daily quota id cannot be empty.");

        return dailyQuotaPolicy.Create(new(
            quotaId,
            QuotaGameId(command),
            command.UserId,
            ChatId(command),
            utcNow,
            IncludeUnlimitedDailyQuota(command)));
    }

    /// <summary>Returns null for a command that does not consume a daily quota.</summary>
    protected virtual string? DailyQuotaId(TCommand command) => null;

    /// <summary>Defaults to the descriptor game id; override for a cross-game action.</summary>
    protected virtual string QuotaGameId(TCommand command) => GameId;

    /// <summary>
    /// Requests a zero-limit snapshot when the policy considers a quota unlimited.
    /// Set false when the action has no quota effect in that situation.
    /// </summary>
    protected virtual bool IncludeUnlimitedDailyQuota(TCommand command) => true;

}

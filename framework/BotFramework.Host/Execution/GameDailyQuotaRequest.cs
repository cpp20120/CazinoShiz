namespace BotFramework.Host.Execution;

/// <summary>
/// Describes the transport-neutral context needed to allocate a daily game quota.
/// </summary>
public sealed record GameDailyQuotaRequest(
    string QuotaId,
    string GameId,
    long UserId,
    long BalanceScopeId,
    DateTimeOffset UtcNow,
    bool IncludeWhenUnlimited);

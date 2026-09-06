namespace BotFramework.Host.Execution;

/// <summary>
/// Maps a game-level daily quota request to the quota snapshot used by atomic execution.
/// Transport and deployment-specific rules belong in an implementation, never in a game descriptor.
/// </summary>
public interface IGameDailyQuotaPolicy
{
    IReadOnlyList<QuotaIdentity> Create(GameDailyQuotaRequest request);
}

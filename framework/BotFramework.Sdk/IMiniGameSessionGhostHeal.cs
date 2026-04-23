namespace BotFramework.Sdk;

/// <summary>
/// Clears <see cref="BotMiniGameSession"/> when it still points at <paramref name="blockingGameId"/>
/// but persistence has no pending work (restart / missed cleanup drift).
/// </summary>
public interface IMiniGameSessionGhostHeal
{
    Task<bool> TryClearGhostIfDbEmptyAsync(long userId, long chatId, string blockingGameId, CancellationToken ct);
}

/// <summary>Test / games that do not register a host implementation.</summary>
public sealed class NullMiniGameSessionGhostHeal : IMiniGameSessionGhostHeal
{
    public Task<bool> TryClearGhostIfDbEmptyAsync(long userId, long chatId, string blockingGameId, CancellationToken ct) =>
        Task.FromResult(false);
}

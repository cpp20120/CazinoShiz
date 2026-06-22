namespace BotFramework.Sdk;
/// <summary>Test / games that do not register a host implementation.</summary>
public sealed class NullMiniGameSessionGhostHeal : IMiniGameSessionGhostHeal
{
    public Task<bool> TryClearGhostIfDbEmptyAsync(long userId, long chatId, string blockingGameId, CancellationToken ct) =>
        Task.FromResult(false);
}

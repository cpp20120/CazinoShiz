namespace BotFramework.Sdk;
public interface IMiniGameSessionStore
{
    Task<MiniGameSessionBeginResult> TryBeginPlaceBetAsync(
        long userId, long chatId, string gameId, CancellationToken ct);

    Task RegisterPlacedBetAsync(long userId, long chatId, string gameId, CancellationToken ct);

    Task ClearCompletedRoundAsync(long userId, long chatId, string gameId, CancellationToken ct);
}

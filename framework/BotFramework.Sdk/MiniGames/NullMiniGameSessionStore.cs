namespace BotFramework.Sdk;
public sealed class NullMiniGameSessionStore : IMiniGameSessionStore
{
    public static readonly NullMiniGameSessionStore Instance = new();

    private NullMiniGameSessionStore() { }

    public Task<MiniGameSessionBeginResult> TryBeginPlaceBetAsync(
        long userId, long chatId, string gameId, CancellationToken ct)
    {
        var ok = BotMiniGameSession.TryBeginPlaceBet(userId, chatId, gameId, out var blocker);
        return Task.FromResult(new MiniGameSessionBeginResult(ok, blocker));
    }

    public Task RegisterPlacedBetAsync(long userId, long chatId, string gameId, CancellationToken ct)
    {
        BotMiniGameSession.RegisterPlacedBet(userId, chatId, gameId);
        return Task.CompletedTask;
    }

    public Task ClearCompletedRoundAsync(long userId, long chatId, string gameId, CancellationToken ct)
    {
        BotMiniGameSession.ClearCompletedRound(userId, chatId, gameId);
        return Task.CompletedTask;
    }
}

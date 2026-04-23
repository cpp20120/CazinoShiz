namespace BotFramework.Sdk;

/// <summary>Shared <see cref="BotMiniGameSession.TryBeginPlaceBet"/> + cross-game ghost heal loop.</summary>
public static class BotMiniGamePlaceBetSession
{
    public static async Task<(bool Ok, string? Blocker)> TryBeginWithGhostHealAsync(
        long userId,
        long chatId,
        string placeBetGameId,
        Func<CancellationToken, Task> clearStaleOwnSlotAsync,
        IMiniGameSessionGhostHeal ghostHeal,
        CancellationToken ct)
    {
        await clearStaleOwnSlotAsync(ct);

        for (var pass = 0; pass < 8; pass++)
        {
            if (BotMiniGameSession.TryBeginPlaceBet(userId, chatId, placeBetGameId, out var blocker))
                return (true, null);
            if (blocker is null)
                return (false, null);
            if (!await ghostHeal.TryClearGhostIfDbEmptyAsync(userId, chatId, blocker, ct))
                return (false, blocker);
        }

        BotMiniGameSession.TryBeginPlaceBet(userId, chatId, placeBetGameId, out var final);
        return (false, final);
    }
}

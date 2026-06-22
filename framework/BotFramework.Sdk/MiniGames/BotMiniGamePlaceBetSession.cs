namespace BotFramework.Sdk;

/// <summary>Shared <see cref="BotMiniGameSession.TryBeginPlaceBet"/> plus cross-game ghost heal loop.</summary>
public static class BotMiniGamePlaceBetSession
{
    public static async Task<(bool Ok, string? Blocker)> TryBeginWithGhostHealAsync(
        long userId,
        long chatId,
        string placeBetGameId,
        Func<CancellationToken, Task> clearStaleOwnSlotAsync,
        IMiniGameSessionGhostHeal ghostHeal,
        IMiniGameSessionStore sessions,
        CancellationToken ct)
    {
        await clearStaleOwnSlotAsync(ct);

        for (var pass = 0; pass < 8; pass++)
        {
            var session = await sessions.TryBeginPlaceBetAsync(userId, chatId, placeBetGameId, ct);
            if (session.Ok)
                return (true, null);
            if (session.BlockingGameId is null)
                return (false, null);
            if (!await ghostHeal.TryClearGhostIfDbEmptyAsync(userId, chatId, session.BlockingGameId, ct))
                return (false, session.BlockingGameId);
        }

        var final = await sessions.TryBeginPlaceBetAsync(userId, chatId, placeBetGameId, ct);
        return final.Ok ? (true, null) : (false, final.BlockingGameId);
    }

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

using System.Collections.Concurrent;

namespace BotFramework.Sdk;

/// <summary>
/// One pending dice mini-game per (user, chat): prevents overlapping bot dice (e.g. /basket then /football)
/// and stale "already pending" confusion across games. Cleared when the round completes (bet row deleted).
/// </summary>
public static class BotMiniGameSession
{
    private sealed record Row(string GameId, long ExpiresTicks);

    private static readonly ConcurrentDictionary<(long UserId, long ChatId), Row> Map = new();

    /// <summary>Test isolation — xUnit constructs a new class instance per test; call from test ctor if tests hit <see cref="TryBeginPlaceBet"/>.</summary>
    public static void DangerousResetAllForTests() => Map.Clear();

    public const int TtlMs = 180_000;

    /// <summary>
    /// Call before debiting for a new bet. Allows same <paramref name="gameId"/> (DB enforces single pending per game).
    /// </summary>
    public static bool TryBeginPlaceBet(long userId, long chatId, string gameId, out string? blockingGameId)
    {
        blockingGameId = null;
        var key = (userId, chatId);
        PruneKey(key);
        if (!Map.TryGetValue(key, out var row))
            return true;
        if (row.GameId == gameId)
            return true;
        if (Environment.TickCount64 > row.ExpiresTicks)
        {
            Map.TryRemove(key, out _);
            return true;
        }

        blockingGameId = row.GameId;
        return false;
    }

    public static void RegisterPlacedBet(long userId, long chatId, string gameId) =>
        Map[(userId, chatId)] = new Row(gameId, Environment.TickCount64 + TtlMs);

    public static void ClearCompletedRound(long userId, long chatId, string gameId)
    {
        var key = (userId, chatId);
        if (Map.TryGetValue(key, out var row) && row.GameId == gameId)
            Map.TryRemove(key, out _);
    }

    private static void PruneKey((long UserId, long ChatId) key)
    {
        if (Map.TryGetValue(key, out var row) && Environment.TickCount64 > row.ExpiresTicks)
            Map.TryRemove(key, out _);
    }
}

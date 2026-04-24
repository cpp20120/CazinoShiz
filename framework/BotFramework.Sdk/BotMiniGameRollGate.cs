using System.Collections.Concurrent;

namespace BotFramework.Sdk;

/// <summary>
/// After <c>/game bet</c> the bot sends the dice; user throws in the same window would cause two
/// animations / races. While this gate is active for (gameId, user, chat), ignore user-originated dice.
/// Cleared when the roll is processed or <see cref="Clear"/>; also expires after <see cref="GraceMs"/>.
/// </summary>
public static class BotMiniGameRollGate
{
    private static readonly ConcurrentDictionary<(string GameId, long UserId, long ChatId), long> UntilTicks = new();

    /// <summary>How long to prefer bot-only rolls after a bet before allowing manual throw again.</summary>
    private const int GraceMs = 60_000;

    public static void ExpectBotRoll(string gameId, long userId, long chatId) =>
        UntilTicks[(gameId, userId, chatId)] = Environment.TickCount64 + GraceMs;

    public static void Clear(string gameId, long userId, long chatId) =>
        UntilTicks.TryRemove((gameId, userId, chatId), out _);

    /// <summary>True → drop this user dice message (bot roll is pending for this bet).</summary>
    public static bool ShouldIgnoreUserThrow(string gameId, long userId, long chatId)
    {
        if (!UntilTicks.TryGetValue((gameId, userId, chatId), out var until))
            return false;
        if (Environment.TickCount64 <= until) return true;
        UntilTicks.TryRemove((gameId, userId, chatId), out _);
        return false;
    }
}

using System.Collections.Concurrent;

namespace Games.Darts;

/// <summary>Maps bot 🎯 dice message id → darts round id (multi-round per chat).</summary>
public static class DartsDiceRoundBinding
{
    private static readonly ConcurrentDictionary<(long ChatId, int MessageId), long> Map = new();

    public static void Bind(long chatId, int messageId, long roundId) =>
        Map[(chatId, messageId)] = roundId;

    public static void Unbind(long chatId, int messageId) =>
        Map.TryRemove((chatId, messageId), out _);

    public static bool TryGetRoundId(long chatId, int messageId, out long roundId) =>
        Map.TryGetValue((chatId, messageId), out roundId);

    /// <summary>Test / process isolation.</summary>
    public static void DangerousResetAllForTests() => Map.Clear();
}

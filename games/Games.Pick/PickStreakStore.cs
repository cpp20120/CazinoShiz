using System.Collections.Concurrent;

namespace Games.Pick;

/// <summary>
/// In-memory consecutive-win counter, keyed by <c>(userId, chatId)</c>. Singleton.
///
/// We accept that streaks reset on bot restart — that's a fair price to avoid a
/// dedicated table for what's essentially flavour state. Chains do not touch
/// this store; only top-level <c>/pick</c> bets do.
/// </summary>
public sealed class PickStreakStore
{
    private readonly ConcurrentDictionary<(long UserId, long ChatId), int> _streaks = new();

    public int Get(long userId, long chatId) =>
        _streaks.TryGetValue((userId, chatId), out var v) ? v : 0;

    public int Increment(long userId, long chatId) =>
        _streaks.AddOrUpdate((userId, chatId), 1, static (_, v) => v + 1);

    public void Reset(long userId, long chatId) =>
        _streaks.TryRemove((userId, chatId), out _);
}

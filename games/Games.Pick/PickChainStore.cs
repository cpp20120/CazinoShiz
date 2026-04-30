using System.Collections.Concurrent;

namespace Games.Pick;

/// <summary>
/// State for a pending double-or-nothing chain offer. The user has just won
/// <see cref="StakeForNext"/> coins and (optionally) wants to re-stake the
/// whole thing on the same variants/backing. Stored in memory keyed by a fresh
/// GUID that we embed in the inline button's callback data.
/// </summary>
public sealed record PickChainState(
    Guid Id,
    long UserId,
    long ChatId,
    string DisplayName,
    int StakeForNext,
    int Depth,
    IReadOnlyList<string> Variants,
    IReadOnlyList<int> BackedIndices,
    DateTimeOffset ExpiresAt);

/// <summary>
/// Singleton coordinator for active chain offers. Atomic <see cref="TryClaim"/>
/// ensures at most one callback can spend a given chain — repeated taps after
/// a successful claim become no-ops.
/// </summary>
public sealed class PickChainStore
{
    private readonly ConcurrentDictionary<Guid, PickChainState> _pending = new();

    public void Add(PickChainState state) => _pending[state.Id] = state;

    /// <summary>
    /// Atomically removes the entry and returns it. If the entry has expired,
    /// it's still removed but null is returned. Re-entrant callbacks racing on
    /// the same guid will see null after the first winner.
    /// </summary>
    public PickChainState? TryClaim(Guid id)
    {
        if (!_pending.TryRemove(id, out var state)) return null;
        return state.ExpiresAt < DateTimeOffset.UtcNow ? null : state;
    }

    /// <summary>
    /// Best-effort sweeper for stale entries. We don't run a background job
    /// just for this — the dictionary stays tiny in practice.
    /// </summary>
    public void Forget(Guid id) => _pending.TryRemove(id, out _);
}

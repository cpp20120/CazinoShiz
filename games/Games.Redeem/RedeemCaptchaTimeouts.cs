// ─────────────────────────────────────────────────────────────────────────────
// RedeemCaptchaTimeouts — singleton that tracks the in-flight captcha-timeout
// task per redeem code so the callback handler can cancel it the moment the
// user picks an answer (right OR wrong).
//
// Why this exists: RedeemHandler is registered Scoped, so each update gets a
// fresh instance — instance fields cannot bridge the gap between the
// `/redeem <code>` message and the inline-keyboard callback that follows.
// Without cancellation the original fire-and-forget Task.Delay always fires
// after CaptchaTimeoutMs and blasts the user with "Вы отвечали слишком долго"
// even after a successful redeem.
//
// Concurrency model: a ConcurrentDictionary keyed by codeGuid. The value is
// the linked CTS that the timeout task awaits on. Schedule() takes care of
// the (rare) race where the same user re-issues `/redeem` for the same code
// before the prior captcha has been answered or timed out — we cancel the
// older CTS first so only the latest captcha can fire its timeout.
// ─────────────────────────────────────────────────────────────────────────────

using System.Collections.Concurrent;

namespace Games.Redeem;

public sealed class RedeemCaptchaTimeouts
{
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _pending = new();

    /// Registers a CTS for `codeGuid` linked to the host's stopping token.
    /// If a CTS is already registered for this code (user retried), the prior
    /// one is cancelled so its timeout task no-ops.
    public CancellationTokenSource Schedule(Guid codeGuid, CancellationToken applicationStopping)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(applicationStopping);
        if (_pending.TryRemove(codeGuid, out var prior))
        {
            try { prior.Cancel(); } catch { }
        }
        _pending[codeGuid] = cts;
        return cts;
    }

    /// Cancels (and removes) the pending timeout for `codeGuid` if any. Called
    /// from the callback handler when the user has picked an answer.
    public bool TryCancel(Guid codeGuid)
    {
        if (_pending.TryRemove(codeGuid, out var cts))
        {
            try { cts.Cancel(); } catch { }
            return true;
        }
        return false;
    }

    /// Atomically removes the entry but only if it still points at the
    /// supplied CTS. Returns true when the caller still owned the slot at
    /// removal time — used by the timeout task to decide whether it's safe to
    /// proceed with the "took too long" message, or whether a concurrent
    /// callback already claimed the slot first.
    public bool Forget(Guid codeGuid, CancellationTokenSource owner)
    {
        return _pending.TryRemove(new KeyValuePair<Guid, CancellationTokenSource>(codeGuid, owner));
    }
}

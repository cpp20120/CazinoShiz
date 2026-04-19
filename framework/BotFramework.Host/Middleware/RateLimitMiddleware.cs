// ─────────────────────────────────────────────────────────────────────────────
// RateLimitMiddleware — per (user, command-type) token bucket.
//
// Stops one user from spamming /roll 20 times/second and hogging a Telegram
// worker. Buckets are keyed by (userId, commandType) so slow games don't
// punish fast games for the same user, and noisy users don't affect quiet
// ones.
//
// Policy comes from IRateLimitPolicy, which the Host resolves from config —
// simple by default ("N commands per M seconds per user"), swappable for
// anything fancier if a module wants custom behavior.
//
// Short-circuits with RateLimitedException when the bucket is empty. Outer
// layers (handler dispatch → update router) catch that and either send a
// friendly "try again in a few seconds" to the user, or silently drop
// depending on the update kind. The middleware itself does NOT send any
// Telegram message — that stays up-stack where the Telegram client is.
// ─────────────────────────────────────────────────────────────────────────────

using BotFramework.Sdk;

namespace BotFramework.Host.Middleware;

public sealed class RateLimitMiddleware(IRateLimitPolicy policy) : ICommandMiddleware
{
    public async Task InvokeAsync(CommandContext ctx, Func<Task> next)
    {
        var key = new RateLimitKey(ctx.Request.UserId, ctx.Command.GetType().Name);
        if (!await policy.TryAcquireAsync(key, ctx.Cancellation))
            throw new RateLimitedException(key);

        await next();
    }
}

public interface IRateLimitPolicy
{
    Task<bool> TryAcquireAsync(RateLimitKey key, CancellationToken ct);
}

public readonly record struct RateLimitKey(long UserId, string CommandType);

public sealed class RateLimitedException(RateLimitKey key)
    : Exception($"rate-limited: user={key.UserId} command={key.CommandType}");

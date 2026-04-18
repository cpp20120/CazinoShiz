using System.Collections.Concurrent;

namespace CasinoShiz.Services.Pipeline;

public sealed partial class RateLimitMiddleware(ILogger<RateLimitMiddleware> logger) : IUpdateMiddleware
{
    private const int Capacity = 10;
    private const double RefillPerSecond = 1.0;

    private static readonly ConcurrentDictionary<long, Bucket> Buckets = new();

    public Task InvokeAsync(UpdateContext ctx, UpdateDelegate next)
    {
        var userId = ctx.UserId;
        if (userId == 0) return next(ctx);

        var bucket = Buckets.GetOrAdd(userId, _ => new Bucket(Capacity, DateTime.UtcNow));
        if (!bucket.TryConsume(DateTime.UtcNow))
        {
            LogRateLimited(userId);
            return Task.CompletedTask;
        }

        return next(ctx);
    }

    private sealed class Bucket(double tokens, DateTime lastRefill)
    {
        private double _tokens = tokens;
        private DateTime _lastRefill = lastRefill;
        private readonly object _lock = new();

        public bool TryConsume(DateTime now)
        {
            lock (_lock)
            {
                var elapsed = (now - _lastRefill).TotalSeconds;
                _tokens = Math.Min(Capacity, _tokens + elapsed * RefillPerSecond);
                _lastRefill = now;
                if (_tokens < 1.0) return false;
                _tokens -= 1.0;
                return true;
            }
        }
    }

    [LoggerMessage(EventId = 1200, Level = LogLevel.Warning, Message = "ratelimit.drop user={UserId}")]
    partial void LogRateLimited(long userId);
}

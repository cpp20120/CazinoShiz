using System.Collections.Concurrent;
using BotFramework.Sdk;
using StackExchange.Redis;

namespace BotFramework.Host.Pipeline;

public sealed partial class RateLimitMiddleware(
    IServiceProvider services,
    ILogger<RateLimitMiddleware> logger) : IUpdateMiddleware
{
    public RateLimitMiddleware(ILogger<RateLimitMiddleware> logger)
        : this(EmptyServiceProvider.Instance, logger) { }

    private const int Capacity = 10;
    private const double RefillPerSecond = 1.0;
    private static readonly TimeSpan RedisWindow = TimeSpan.FromSeconds(10);

    private static readonly ConcurrentDictionary<long, Bucket> Buckets = new();

    public async Task InvokeAsync(UpdateContext ctx, UpdateDelegate next)
    {
        var userId = ctx.UserId;
        if (userId == 0)
        {
            await next(ctx);
            return;
        }

        var redis = services.GetService<IConnectionMultiplexer>();
        if (redis is not null)
        {
            var db = redis.GetDatabase();
            var key = $"ratelimit:update:{userId}:{DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 10}";
            var count = await db.StringIncrementAsync(key);
            if (count == 1)
                await db.KeyExpireAsync(key, RedisWindow + TimeSpan.FromSeconds(2));
            if (count > Capacity)
            {
                LogRateLimited(userId);
                return;
            }

            await next(ctx);
            return;
        }

        var bucket = Buckets.GetOrAdd(userId, _ => new Bucket(Capacity, DateTime.UtcNow));
        if (!bucket.TryConsume(DateTime.UtcNow))
        {
            LogRateLimited(userId);
            return;
        }

        await next(ctx);
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public static readonly EmptyServiceProvider Instance = new();
        public object? GetService(Type serviceType) => null;
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

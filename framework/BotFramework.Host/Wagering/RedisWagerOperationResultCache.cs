using System.Text.Json;
using BotFramework.Contracts.Wagering;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace BotFramework.Host.Wagering;

public sealed class RedisWagerOperationResultCache(IServiceProvider services) : IWagerOperationResultCache
{
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(24);

    public async Task<WagerOperation?> GetAsync(string operationId, CancellationToken ct)
    {
        var db = Database;
        if (db is null) return null;
        var value = await db.StringGetAsync(Key(operationId)).WaitAsync(ct);
        return value.HasValue ? JsonSerializer.Deserialize<WagerOperation>(value.ToString()) : null;
    }

    public async Task SetAsync(WagerOperation operation, CancellationToken ct)
    {
        var db = Database;
        if (db is null) return;
        await db.StringSetAsync(Key(operation.OperationId), JsonSerializer.Serialize(operation), Ttl).WaitAsync(ct);
    }

    private IDatabase? Database => services.GetService<IConnectionMultiplexer>() is { IsConnected: true } redis
        ? redis.GetDatabase()
        : null;
    private static RedisKey Key(string operationId) => $"wager:operation:v1:{operationId}";
}

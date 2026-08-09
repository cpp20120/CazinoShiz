using System.Text.Json;
using BotFramework.Contracts.Messaging;
using BotFramework.Contracts.Wagering;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace BotFramework.Host.Wagering;

/// <summary>Best-effort terminal operation cache; PostgreSQL remains authoritative.</summary>
public sealed class RedisWagerOperationProjection(IServiceProvider services, IWagerOperationStore operations)
    : IIntegrationEventHandler<WagerSettled>
{
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(24);

    public async Task HandleAsync(WagerSettled settled, CancellationToken ct)
    {
        var redis = services.GetService<IConnectionMultiplexer>();
        if (redis is not { IsConnected: true }) return;
        var operation = await operations.GetAsync(settled.OperationId, ct);
        if (operation is null) return;
        await redis.GetDatabase().StringSetAsync(Key(settled.OperationId), JsonSerializer.Serialize(operation), Ttl).WaitAsync(ct);
    }

    private static RedisKey Key(string operationId) => $"wager:operation:v1:{operationId}";
}

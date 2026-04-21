using System.Text.Json;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Telegram.Bot.Types;

namespace BotFramework.Host.Redis;

public sealed class UpdateStreamPublisher(IConnectionMultiplexer redis, IOptions<RedisOptions> opts)
{
    private readonly RedisOptions _opts = opts.Value;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public async Task PublishAsync(Update update, CancellationToken ct)
    {
        var chatId = ExtractChatId(update);
        var partition = (int)(Math.Abs(chatId) % _opts.PartitionCount);
        var key = $"{_opts.StreamKeyPrefix}:{partition}";
        var json = JsonSerializer.Serialize(update, JsonOpts);
        await redis.GetDatabase().StreamAddAsync(key, [new NameValueEntry("u", json)]);
    }

    private static long ExtractChatId(Update u) =>
        u.Message?.Chat.Id
        ?? u.CallbackQuery?.Message?.Chat.Id
        ?? u.ChannelPost?.Chat.Id
        ?? u.EditedMessage?.Chat.Id
        ?? u.Id;
}

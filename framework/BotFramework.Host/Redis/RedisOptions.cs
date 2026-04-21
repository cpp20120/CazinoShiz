namespace BotFramework.Host.Redis;

public sealed class RedisOptions
{
    public const string SectionName = "Redis";

    public bool Enabled { get; init; } = false;
    public string ConnectionString { get; init; } = "";
    public int PartitionCount { get; init; } = 8;
    public string StreamKeyPrefix { get; init; } = "bot:updates";
    public string ConsumerGroup { get; init; } = "workers";
}

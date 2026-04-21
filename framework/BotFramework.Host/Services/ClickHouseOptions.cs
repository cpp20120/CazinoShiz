namespace BotFramework.Host.Services;

public sealed class ClickHouseOptions
{
    public const string SectionName = "ClickHouse";

    public bool Enabled { get; set; } = false;
    public string Host { get; set; } = "";
    public string User { get; set; } = "default";
    public string Password { get; set; } = "";
    public string Database { get; set; } = "default";
    public string Table { get; set; } = "events_v2";
    public string Project { get; set; } = "bot";
    public int BufferSize { get; set; } = 100;
    public int FlushIntervalMs { get; set; } = 5_000;
}

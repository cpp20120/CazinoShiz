namespace CasinoShiz.Configuration;

public sealed class ClickHouseOptions
{
    public const string SectionName = "ClickHouse";

    public bool Enabled { get; init; } = true;
    public string Host { get; init; } = "http://localhost:8123";
    public string User { get; init; } = "default";
    public string Password { get; init; } = "";
    public string Database { get; init; } = "analytics";
    public string Table { get; init; } = "events";
    public string Project { get; init; } = "cazinoshiz";
    public int BufferSize { get; init; } = 10;
    public int FlushIntervalMs { get; init; } = 3000;
}

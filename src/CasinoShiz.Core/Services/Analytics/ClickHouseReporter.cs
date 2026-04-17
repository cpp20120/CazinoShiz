using System.Text.Json;
using CasinoShiz.Configuration;
using ClickHouse.Client.ADO;
using Microsoft.Extensions.Options;

namespace CasinoShiz.Services.Analytics;

public sealed partial class ClickHouseReporter : IDisposable
{
    private readonly ClickHouseOptions _options;
    private readonly ILogger<ClickHouseReporter> _logger;
    private readonly List<EventData> _buffer = [];
    private readonly Lock _lock = new();
    private readonly Timer? _flushTimer;
    private readonly ClickHouseConnection? _connection;

    public ClickHouseReporter(IOptions<ClickHouseOptions> options, ILogger<ClickHouseReporter> logger)
    {
        _options = options.Value;
        _logger = logger;

        if (!_options.Enabled)
        {
            LogClickhouseReporterIsDisabled();
            return;
        }

        try
        {
            _connection = new ClickHouseConnection(BuildConnectionString(_options));

            _flushTimer = new Timer(_ => FlushBuffer().GetAwaiter().GetResult(), null,
                TimeSpan.FromMilliseconds(_options.FlushIntervalMs),
                TimeSpan.FromMilliseconds(_options.FlushIntervalMs));
        }
        catch (Exception ex)
        {
            LogFailedToInitializeClickhouseReporterAnalyticsWillBeDisabled(ex);
            _connection = null;
        }
    }

    private static string BuildConnectionString(ClickHouseOptions o)
    {
        var parts = new List<string>();
        if (Uri.TryCreate(o.Host, UriKind.Absolute, out var uri))
        {
            parts.Add($"Host={uri.Host}");
            if (!uri.IsDefaultPort) parts.Add($"Port={uri.Port}");
            parts.Add($"Protocol={uri.Scheme}");
        }
        else
        {
            parts.Add($"Host={o.Host}");
        }
        parts.Add($"Username={o.User}");
        parts.Add($"Password={o.Password}");
        parts.Add($"Database={o.Database}");
        return string.Join(";", parts);
    }

    public void SendEvent(EventData evt)
    {
        if (!_options.Enabled) return;

        lock (_lock)
        {
            _buffer.Add(evt);
            if (_buffer.Count >= _options.BufferSize)
                _ = Task.Run(() => FlushBuffer());
        }
    }

    public void SendEvents(IEnumerable<EventData> events)
    {
        if (!_options.Enabled) return;

        lock (_lock)
        {
            _buffer.AddRange(events);
            if (_buffer.Count >= _options.BufferSize)
                _ = Task.Run(() => FlushBuffer());
        }
    }

    private async Task FlushBuffer()
    {
        List<EventData> eventsToSend;
        lock (_lock)
        {
            if (_buffer.Count == 0) return;
            eventsToSend = [.. _buffer];
            _buffer.Clear();
        }

        try
        {
            if (_connection == null) return;

            await using var cmd = _connection.CreateCommand();
            var values = string.Join(",", eventsToSend.Select(e =>
            {
                var payload = e.Payload != null ? JsonSerializer.Serialize(e.Payload) : null;
                var escapedPayload = payload?.Replace("'", "\\'");
                return $"('{e.EventType ?? "fallbackEvent"}', '{_options.Project}', {{}}, {(escapedPayload != null ? $"'{escapedPayload}'" : "NULL")})";
            }));

            cmd.CommandText = $"INSERT INTO {_options.Table} (event_type, project, params, payload) VALUES {values}";
            await cmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            LogFailedToFlushClickhouseBufferReturningEvents(ex);
            lock (_lock)
            {
                _buffer.InsertRange(0, eventsToSend);
            }
        }
    }

    public async Task CreateTableIfNotExists()
    {
        if (!_options.Enabled || _connection == null) return;

        try
        {
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText = $"""
                CREATE TABLE IF NOT EXISTS {_options.Table} (
                    event_id UUID DEFAULT generateUUIDv4(),
                    event_type String,
                    project String,
                    params Map(String, String),
                    payload Nullable(String),
                    created_at DateTime DEFAULT now()
                ) ENGINE = MergeTree()
                ORDER BY (project, event_type, created_at)
                SETTINGS index_granularity = 8192
                """;
            await cmd.ExecuteNonQueryAsync();
            LogClickhouseTableTableIsReady(_options.Table);
        }
        catch (Exception ex)
        {
            LogFailedToCreateClickhouseTable(ex);
        }
    }

    public void Dispose()
    {
        _flushTimer?.Dispose();
        FlushBuffer().GetAwaiter().GetResult();
        _connection?.Dispose();
    }

    [LoggerMessage(LogLevel.Information, "ClickHouse reporter is disabled")]
    partial void LogClickhouseReporterIsDisabled();

    [LoggerMessage(LogLevel.Error, "Failed to initialize ClickHouse reporter, analytics will be disabled")]
    partial void LogFailedToInitializeClickhouseReporterAnalyticsWillBeDisabled(Exception exception);

    [LoggerMessage(LogLevel.Error, "Failed to flush ClickHouse buffer, returning events")]
    partial void LogFailedToFlushClickhouseBufferReturningEvents(Exception exception);

    [LoggerMessage(LogLevel.Information, "ClickHouse table {Table} is ready")]
    partial void LogClickhouseTableTableIsReady(string table);

    [LoggerMessage(LogLevel.Error, "Failed to create ClickHouse table")]
    partial void LogFailedToCreateClickhouseTable(Exception exception);
}

public sealed class EventData
{
    public string? EventType { get; init; }
    public object? Payload { get; init; }
    public Dictionary<string, string>? Params { get; init; }
}

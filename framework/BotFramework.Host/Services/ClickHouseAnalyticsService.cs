// ─────────────────────────────────────────────────────────────────────────────
// ClickHouseAnalyticsService — the framework's IAnalyticsService, with a
// buffered ClickHouse sink.
//
// Schema (events_v2):
//   event_id    UUID          auto
//   event_type  String        "darts.throw_completed", "host.webhook", …
//   module      String        "darts" (split_part(event_type, '.', 1))
//   project     String        distribution id (ClickHouse__Project)
//   user_id     Int64         0 when not applicable
//   params      Map(String,String)   tag key/value pairs, stringified
//   payload     String        JSON; module-call payload OR full domain event
//   created_at  DateTime64(3)
//
// Inserts use ClickHouseBulkCopy — parameterized, no string-concat SQL.
//
// Two entry points:
//   • Track(moduleId, eventName, tags)  → legacy per-module analytics.
//   • TrackDomainEvent(IDomainEvent ev) → mirror of the event bus. Properties
//     are reflected into tags so Grafana can filter on params without
//     JSONExtract.
//
// Track is synchronous and non-blocking — all I/O runs on a PeriodicTimer-
// driven flush loop so game handlers never wait for ClickHouse.
// ─────────────────────────────────────────────────────────────────────────────

using System.Reflection;
using System.Text.Json;
using BotFramework.Sdk;
using ClickHouse.Client.ADO;
using ClickHouse.Client.Copy;
using Microsoft.Extensions.Options;

namespace BotFramework.Host.Services;

public sealed partial class ClickHouseAnalyticsService(
    IOptions<ClickHouseOptions> options,
    ILogger<ClickHouseAnalyticsService> logger)
    : IAnalyticsService, IHostedService, IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

    private readonly ClickHouseOptions _options = options.Value;
    private readonly Lock _lock = new();
    private readonly List<EventData> _buffer = [];
    private ClickHouseConnection? _connection;
    private PeriodicTimer? _flushTimer;
    private CancellationTokenSource? _loopCts;
    private Task? _flushLoop;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            LogReporterDisabled();
            return;
        }

        if (string.IsNullOrWhiteSpace(_options.Host))
            throw new InvalidOperationException(
                "ClickHouse:Enabled is true but ClickHouse:Host is not set.");

        try
        {
            _connection = new ClickHouseConnection(BuildConnectionString(_options));
            await _connection.OpenAsync(cancellationToken);
            await CreateTableIfNotExistsAsync(cancellationToken);

            _loopCts = new CancellationTokenSource();
            _flushTimer = new PeriodicTimer(TimeSpan.FromMilliseconds(_options.FlushIntervalMs));
            _flushLoop = RunFlushLoopAsync(_loopCts.Token);
        }
        catch (Exception ex)
        {
            LogInitFailed(ex);
            if (_connection != null) await _connection.DisposeAsync();
            _connection = null;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _flushTimer?.Dispose();
        await _loopCts?.CancelAsync()!;
        if (_flushLoop != null)
        {
            try { await _flushLoop; }
            catch (OperationCanceledException) { }
        }

        await FlushBufferAsync();
        if (_connection != null) await _connection.DisposeAsync();
        _connection = null;
    }

    public void Track(string moduleId, string eventName, IReadOnlyDictionary<string, object?> tags)
    {
        if (!_options.Enabled) return;

        var eventType = $"{moduleId}.{eventName}";
        var userId = ExtractUserId(tags);
        var paramsMap = ToParamsMap(tags);
        var payload = JsonSerializer.Serialize(tags, JsonOpts);
        Enqueue(new EventData(eventType, moduleId, userId, paramsMap, payload, DateTime.UtcNow));
    }

    public void TrackDomainEvent(IDomainEvent ev)
    {
        if (!_options.Enabled) return;

        var type = ev.GetType();
        var tags = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        long userId = 0;

        foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (p.Name == nameof(IDomainEvent.EventType)) continue;
            var v = p.GetValue(ev);
            tags[p.Name] = v;
            if (userId == 0 && v is long l &&
                (p.Name is "UserId" or "HostUserId" or "IssuedBy" or "RedeemedBy"))
                userId = l;
        }

        var module = SplitModule(ev.EventType);
        var paramsMap = ToParamsMap(tags);
        var payload = JsonSerializer.Serialize(ev, type, JsonOpts);
        Enqueue(new EventData(ev.EventType, module, userId, paramsMap, payload, DateTime.UtcNow));
    }

    private void Enqueue(EventData evt)
    {
        var shouldFlush = false;
        lock (_lock)
        {
            _buffer.Add(evt);
            if (_buffer.Count >= _options.BufferSize) shouldFlush = true;
        }
        if (shouldFlush) _ = Task.Run(FlushBufferAsync);
    }

    private static long ExtractUserId(IReadOnlyDictionary<string, object?> tags)
    {
        foreach (var key in new[] { "user_id", "UserId", "host_user_id", "HostUserId" })
        {
            if (tags.TryGetValue(key, out var v) && v is not null)
            {
                if (v is long l) return l;
                if (v is int i) return i;
                if (long.TryParse(v.ToString(), out var parsed)) return parsed;
            }
        }
        return 0;
    }

    private static Dictionary<string, string> ToParamsMap(IReadOnlyDictionary<string, object?> tags)
    {
        var map = new Dictionary<string, string>(tags.Count);
        foreach (var (k, v) in tags)
        {
            if (v is null) continue;
            map[k] = v is string s ? s : v.ToString() ?? "";
        }
        return map;
    }

    private static string SplitModule(string eventType)
    {
        var dot = eventType.IndexOf('.');
        return dot < 0 ? eventType : eventType[..dot];
    }

    private async Task RunFlushLoopAsync(CancellationToken ct)
    {
        while (_flushTimer != null && await _flushTimer.WaitForNextTickAsync(ct))
        {
            await FlushBufferAsync();
        }
    }

    private async Task FlushBufferAsync()
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
            if (_connection is null) return;

            using var bulk = new ClickHouseBulkCopy(_connection)
            {
                DestinationTableName = $"{_options.Database}.{_options.Table}",
                ColumnNames = ["event_type", "module", "project", "user_id", "params", "payload", "created_at"],
                BatchSize = Math.Max(_options.BufferSize, 100),
            };
            await bulk.InitAsync();

            var rows = eventsToSend.Select(e => new object[]
            {
                e.EventType, e.Module, _options.Project, e.UserId, e.Params, e.Payload, e.CreatedAt,
            });
            await bulk.WriteToServerAsync(rows);
        }
        catch (Exception ex)
        {
            LogFlushFailed(ex);
            lock (_lock) _buffer.InsertRange(0, eventsToSend);
        }
    }

    private async Task CreateTableIfNotExistsAsync(CancellationToken ct)
    {
        if (_connection is null) return;

        await using (var dbCmd = _connection.CreateCommand())
        {
            dbCmd.CommandText = $"CREATE DATABASE IF NOT EXISTS {_options.Database}";
            await dbCmd.ExecuteNonQueryAsync(ct);
        }

        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = $$"""
            CREATE TABLE IF NOT EXISTS {{_options.Database}}.{{_options.Table}} (
                event_id    UUID               DEFAULT generateUUIDv4(),
                event_type  LowCardinality(String),
                module      LowCardinality(String),
                project     LowCardinality(String),
                user_id     Int64,
                params      Map(String, String),
                payload     String,
                created_at  DateTime64(3)      DEFAULT now64(3)
            ) ENGINE = MergeTree()
            ORDER BY (project, module, event_type, created_at)
            SETTINGS index_granularity = 8192
            """;
        await cmd.ExecuteNonQueryAsync(ct);
        LogTableReady($"{_options.Database}.{_options.Table}");
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

    public async ValueTask DisposeAsync()
    {
        _flushTimer?.Dispose();
        _loopCts?.Dispose();
        if (_connection != null) await _connection.DisposeAsync();
    }

    private sealed record EventData(
        string EventType,
        string Module,
        long UserId,
        Dictionary<string, string> Params,
        string Payload,
        DateTime CreatedAt);

    [LoggerMessage(LogLevel.Information, "clickhouse.disabled")]
    partial void LogReporterDisabled();

    [LoggerMessage(LogLevel.Error, "clickhouse.init_failed analytics=disabled")]
    partial void LogInitFailed(Exception exception);

    [LoggerMessage(LogLevel.Error, "clickhouse.flush_failed buffer_restored")]
    partial void LogFlushFailed(Exception exception);

    [LoggerMessage(LogLevel.Information, "clickhouse.table_ready name={Table}")]
    partial void LogTableReady(string table);
}

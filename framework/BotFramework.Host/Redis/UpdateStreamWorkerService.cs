using System.Text.Json;
using BotFramework.Host.Pipeline;
using BotFramework.Sdk;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace BotFramework.Host.Redis;

public sealed partial class UpdateStreamWorkerService(
    IConnectionMultiplexer redis,
    IServiceProvider services,
    IOptions<RedisOptions> opts,
    ILogger<UpdateStreamWorkerService> logger) : IHostedService
{
    private readonly RedisOptions _opts = opts.Value;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };
    private CancellationTokenSource? _cts;
    private Task? _running;

    public async Task StartAsync(CancellationToken ct)
    {
        var db = redis.GetDatabase();
        for (var i = 0; i < _opts.PartitionCount; i++)
        {
            var key = StreamKey(i);
            try
            {
                await EnsureStreamAndConsumerGroupAsync(db, key, ct);
            }
            catch (Exception ex)
            {
                LogStreamGroupInitFailed(i, key, ex);
            }
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var tasks = Enumerable.Range(0, _opts.PartitionCount)
            .Select(p => RunPartitionAsync(p, _cts.Token))
            .ToArray();
        _running = Task.WhenAll(tasks);
    }

    public async Task StopAsync(CancellationToken ct)
    {
        _cts?.Cancel();
        if (_running is not null)
            try { await _running.WaitAsync(ct); } catch { }
    }

    private string StreamKey(int partition) => $"{_opts.StreamKeyPrefix}:{partition}";

    /// <summary>
    /// <c>XREADGROUP</c> returns NOGROUP if the stream was never created or the group is missing
    /// (e.g. startup XGROUP failed silently, empty Redis, or key evicted). MKSTREAM + create group fixes it.
    /// </summary>
    private async Task EnsureStreamAndConsumerGroupAsync(IDatabase db, string streamKey, CancellationToken ct)
    {
        try
        {
            await db.StreamCreateConsumerGroupAsync(
                streamKey,
                _opts.ConsumerGroup,
                "$",
                createStream: true);
        }
        catch (RedisException ex) when (ex.Message.Contains("BUSYGROUP", StringComparison.Ordinal))
        {
            // Group (and stream) already exist.
        }
    }

    private static bool IsNoGroupError(Exception ex) =>
        ex.Message.Contains("NOGROUP", StringComparison.Ordinal);

    private async Task RunPartitionAsync(int partition, CancellationToken ct)
    {
        var db = redis.GetDatabase();
        var streamKey = StreamKey(partition);
        var consumer = $"partition:{partition}";

        while (!ct.IsCancellationRequested)
        {
            try
            {
                // Recover pending messages first so failed deliveries get retried.
                var pending = await db.StreamReadGroupAsync(
                    streamKey, _opts.ConsumerGroup, consumer, "0", count: 10);
                if (pending.Length > 0)
                {
                    foreach (var entry in pending)
                        await HandleEntryAsync(db, streamKey, partition, entry, ct);
                    continue;
                }

                var entries = await db.StreamReadGroupAsync(
                    streamKey, _opts.ConsumerGroup, consumer, ">", count: 1);

                if (entries.Length == 0)
                {
                    await Task.Delay(50, ct);
                    continue;
                }

                foreach (var entry in entries)
                {
                    await HandleEntryAsync(db, streamKey, partition, entry, ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception ex) when (IsNoGroupError(ex))
            {
                try
                {
                    await EnsureStreamAndConsumerGroupAsync(db, streamKey, ct);
                }
                catch (Exception ensureEx)
                {
                    LogWorkerError(ensureEx, partition);
                }
            }
            catch (Exception ex)
            {
                LogWorkerError(ex, partition);
                try { await Task.Delay(1_000, ct); } catch { return; }
            }
        }
    }

    private async Task HandleEntryAsync(
        IDatabase db,
        string streamKey,
        int partition,
        StreamEntry entry,
        CancellationToken ct)
    {
        try
        {
            await ProcessAsync(entry, ct);
            await db.StreamAcknowledgeAsync(streamKey, _opts.ConsumerGroup, entry.Id);
            await db.KeyDeleteAsync(RetryKey(partition, entry.Id.ToString()));
        }
        catch (Exception ex)
        {
            var retryKey = RetryKey(partition, entry.Id.ToString());
            var attempts = await db.StringIncrementAsync(retryKey);
            await db.KeyExpireAsync(retryKey, TimeSpan.FromSeconds(_opts.RetryCounterTtlSeconds));
            LogProcessingFailed(ex, partition, entry.Id.ToString(), (long)attempts);

            if (attempts < _opts.MaxProcessingAttempts)
                return;

            var payload = (string?)entry["u"] ?? "";
            await db.StreamAddAsync(_opts.DeadLetterStreamKey,
            [
                new NameValueEntry("partition", partition),
                new NameValueEntry("stream", streamKey),
                new NameValueEntry("entry_id", entry.Id.ToString()),
                new NameValueEntry("attempts", attempts.ToString()),
                new NameValueEntry("error_type", ex.GetType().FullName ?? "Exception"),
                new NameValueEntry("error_message", ex.Message),
                new NameValueEntry("u", payload),
                new NameValueEntry("failed_at", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()),
            ]);

            await db.StreamAcknowledgeAsync(streamKey, _opts.ConsumerGroup, entry.Id);
            await db.KeyDeleteAsync(retryKey);
            LogMovedToDlq(partition, entry.Id.ToString(), (long)attempts);
        }
    }

    private string RetryKey(int partition, string entryId) =>
        $"{_opts.StreamKeyPrefix}:retry:{partition}:{entryId}";

    private async Task ProcessAsync(StreamEntry entry, CancellationToken ct)
    {
        var json = (string?)entry["u"];
        if (json is null) return;

        var update = JsonSerializer.Deserialize<Update>(json, JsonOpts);
        if (update is null) return;

        using var scope = services.CreateScope();
        var pipeline = scope.ServiceProvider.GetRequiredService<UpdatePipeline>();
        var bot = scope.ServiceProvider.GetRequiredService<ITelegramBotClient>();
        var ctx = new UpdateContext(bot, update, scope.ServiceProvider, ct);
        await pipeline.InvokeAsync(ctx);
    }

    [LoggerMessage(LogLevel.Error, "update_worker.error partition={Partition}")]
    partial void LogWorkerError(Exception ex, int partition);

    [LoggerMessage(LogLevel.Warning, "update_stream.xgroup_init_failed partition={Partition} key={StreamKey}")]
    partial void LogStreamGroupInitFailed(int partition, string streamKey, Exception ex);

    [LoggerMessage(LogLevel.Warning, "update_worker.processing_failed partition={Partition} id={EntryId} attempts={Attempts}")]
    partial void LogProcessingFailed(Exception ex, int partition, string entryId, long attempts);

    [LoggerMessage(LogLevel.Error, "update_worker.moved_to_dlq partition={Partition} id={EntryId} attempts={Attempts}")]
    partial void LogMovedToDlq(int partition, string entryId, long attempts);
}

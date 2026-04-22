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
        var consumer = $"{Environment.MachineName}:{partition}";

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var entries = await db.StreamReadGroupAsync(
                    streamKey, _opts.ConsumerGroup, consumer, ">", count: 1);

                if (entries.Length == 0)
                {
                    await Task.Delay(50, ct);
                    continue;
                }

                foreach (var entry in entries)
                {
                    try
                    {
                        await ProcessAsync(entry, ct);
                    }
                    catch (Exception ex)
                    {
                        LogProcessingFailed(ex, partition, entry.Id.ToString());
                    }
                    finally
                    {
                        await db.StreamAcknowledgeAsync(streamKey, _opts.ConsumerGroup, entry.Id);
                    }
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

    [LoggerMessage(LogLevel.Warning, "update_worker.processing_failed partition={Partition} id={EntryId}")]
    partial void LogProcessingFailed(Exception ex, int partition, string entryId);
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;
using Minio.Exceptions;

namespace BotFramework.Rendering;

internal sealed partial class MinioRenderArtifactStore : IRenderArtifactStore, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IMinioClient _client;
    private readonly MinioRenderingOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<MinioRenderArtifactStore> _logger;
    private readonly SemaphoreSlim _bucketGate = new(1, 1);
    private bool _bucketReady;

    public MinioRenderArtifactStore(
        IOptions<RenderingOptions> options,
        TimeProvider timeProvider,
        ILogger<MinioRenderArtifactStore> logger)
    {
        _options = options.Value.Minio;
        _timeProvider = timeProvider;
        _logger = logger;
        _client = new MinioClient()
            .WithEndpoint(_options.Endpoint)
            .WithCredentials(_options.AccessKey, _options.SecretKey)
            .WithSSL(_options.Secure)
            .Build();
    }

    public async ValueTask<RenderedArtifact?> FindAsync(RenderKey key, CancellationToken ct)
    {
        await EnsureBucketAsync(ct);
        try
        {
            var bytes = await DownloadAsync(key.ObjectName, ct);
            return new RenderedArtifact(
                key,
                bytes,
                $"render.{key.Extension.TrimStart('.')}",
                _timeProvider.GetUtcNow(),
                key.ObjectName,
                true);
        }
        catch (ObjectNotFoundException)
        {
            return null;
        }
    }

    public async ValueTask<RenderedArtifact> PutAsync(RenderKey key, RenderOutput output, CancellationToken ct)
    {
        await EnsureBucketAsync(ct);
        await UploadAsync(key.ObjectName, key.ContentType, output.Content, ct);
        return new RenderedArtifact(
            key,
            output.Content,
            output.FileName ?? $"render.{key.Extension.TrimStart('.')}",
            _timeProvider.GetUtcNow(),
            key.ObjectName,
            false);
    }

    public async ValueTask RecordHistoryAsync(RenderHistoryEntry entry, CancellationToken ct)
    {
        await EnsureBucketAsync(ct);
        var json = JsonSerializer.SerializeToUtf8Bytes(entry, JsonOptions);
        await UploadAsync(HistoryObjectName(entry), "application/json", json, ct);
    }

    public async IAsyncEnumerable<RenderHistoryEntry> ListHistoryAsync(
        string gameId,
        string aggregateId,
        int take,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await EnsureBucketAsync(ct);
        var prefix = $"history/{Segment(gameId)}/{Segment(aggregateId)}/";
        var names = new List<string>();
        var args = new ListObjectsArgs()
            .WithBucket(_options.Bucket)
            .WithPrefix(prefix)
            .WithRecursive(true);
        await foreach (var item in _client.ListObjectsEnumAsync(args, ct))
            names.Add(item.Key);

        foreach (var name in names.OrderDescending(StringComparer.Ordinal).Take(Math.Max(0, take)))
        {
            ct.ThrowIfCancellationRequested();
            var json = await DownloadAsync(name, ct);
            var entry = JsonSerializer.Deserialize<RenderHistoryEntry>(json, JsonOptions);
            if (entry is not null) yield return entry;
        }
    }

    private async Task EnsureBucketAsync(CancellationToken ct)
    {
        if (_bucketReady) return;
        await _bucketGate.WaitAsync(ct);
        try
        {
            if (_bucketReady) return;
            var exists = await _client.BucketExistsAsync(
                new BucketExistsArgs().WithBucket(_options.Bucket), ct);
            if (!exists)
            {
                await _client.MakeBucketAsync(
                    new MakeBucketArgs().WithBucket(_options.Bucket), ct);
                LogBucketCreated(_options.Bucket);
            }
            _bucketReady = true;
        }
        finally
        {
            _bucketGate.Release();
        }
    }

    private async Task<byte[]> DownloadAsync(string objectName, CancellationToken ct)
    {
        await using var buffer = new MemoryStream();
        var args = new GetObjectArgs()
            .WithBucket(_options.Bucket)
            .WithObject(objectName)
            .WithCallbackStream(stream => stream.CopyToAsync(buffer, ct));
        await _client.GetObjectAsync(args, ct);
        return buffer.ToArray();
    }

    private async Task UploadAsync(
        string objectName,
        string contentType,
        byte[] bytes,
        CancellationToken ct)
    {
        await using var stream = new MemoryStream(bytes, writable: false);
        var args = new PutObjectArgs()
            .WithBucket(_options.Bucket)
            .WithObject(objectName)
            .WithStreamData(stream)
            .WithObjectSize(stream.Length)
            .WithContentType(contentType);
        await _client.PutObjectAsync(args, ct);
    }

    private static string HistoryObjectName(RenderHistoryEntry entry) =>
        $"history/{Segment(entry.GameId)}/{Segment(entry.AggregateId)}/{entry.CreatedAt:yyyyMMddTHHmmssfffffffZ}-{Segment(entry.MatchId)}.json";

    private static string Segment(string value) => string.Concat(value.Select(static ch =>
        char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' or '.' ? ch : '-'));

    public void Dispose()
    {
        _bucketGate.Dispose();
        _client.Dispose();
    }

    [LoggerMessage(EventId = 6100, Level = LogLevel.Information, Message = "render.minio bucket created bucket={Bucket}")]
    private partial void LogBucketCreated(string bucket);
}

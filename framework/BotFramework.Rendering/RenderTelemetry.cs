using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace BotFramework.Rendering;

internal static class RenderTelemetry
{
    private const string InstrumentationName = "BotFramework.Rendering";
    private static readonly Meter Meter = new(InstrumentationName);
    private static readonly Counter<long> CacheHits = Meter.CreateCounter<long>("render.cache.hits");
    private static readonly Counter<long> CacheMisses = Meter.CreateCounter<long>("render.cache.misses");
    private static readonly Counter<long> Failures = Meter.CreateCounter<long>("render.failures");
    private static readonly Counter<long> StoreFailures = Meter.CreateCounter<long>("render.store.failures");
    private static readonly Histogram<double> Duration = Meter.CreateHistogram<double>("render.duration", "ms");
    private static readonly UpDownCounter<long> QueueDepth = Meter.CreateUpDownCounter<long>("render.queue.depth");

    public static void Hit(string renderer) => CacheHits.Add(1, Tags(renderer));
    public static void Miss(string renderer) => CacheMisses.Add(1, Tags(renderer));
    public static void Failed(string renderer) => Failures.Add(1, Tags(renderer));
    public static void StoreFailed(string renderer, string operation) =>
        StoreFailures.Add(1, new TagList { { "renderer", renderer }, { "operation", operation } });
    public static void Enqueued(string renderer) => QueueDepth.Add(1, Tags(renderer));
    public static void Dequeued(string renderer) => QueueDepth.Add(-1, Tags(renderer));
    public static void Completed(string renderer, TimeSpan elapsed) =>
        Duration.Record(elapsed.TotalMilliseconds, Tags(renderer));

    private static TagList Tags(string renderer) => new() { { "renderer", renderer } };
}

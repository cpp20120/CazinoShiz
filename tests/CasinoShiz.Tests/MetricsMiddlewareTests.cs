using BotFramework.Host;
using BotFramework.Host.Middleware;
using BotFramework.Sdk;
using Xunit;

namespace CasinoShiz.Tests;

public class MetricsMiddlewareTests
{
    private sealed class FakeMetrics : IMetrics
    {
        public List<(string Name, IReadOnlyDictionary<string, string>? Tags, double Amount)> Counters { get; } = [];
        public List<(string Name, double Value, IReadOnlyDictionary<string, string>? Tags)> Histograms { get; } = [];

        public void CounterInc(string name, IReadOnlyDictionary<string, string>? tags = null, double amount = 1)
            => Counters.Add((name, tags, amount));

        public void HistogramObserve(string name, double value, IReadOnlyDictionary<string, string>? tags = null)
            => Histograms.Add((name, value, tags));

        public void GaugeSet(string name, double value, IReadOnlyDictionary<string, string>? tags = null) { }
    }

    private sealed record FakeCommand(string ModuleId) : ICommand;

    private static CommandContext MakeCtx(string moduleId = "poker") =>
        new(new FakeCommand(moduleId), RequestContextAccessor.Anonymous, default);

    // ── Counter ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task InvokeAsync_Success_IncrementsCounter()
    {
        var metrics = new FakeMetrics();
        var mw = new MetricsMiddleware(metrics);
        await mw.InvokeAsync(MakeCtx(), () => Task.CompletedTask);
        Assert.Single(metrics.Counters);
        Assert.Equal("bot_commands_total", metrics.Counters[0].Name);
    }

    [Fact]
    public async Task InvokeAsync_Success_CounterOutcomeIsOk()
    {
        var metrics = new FakeMetrics();
        var mw = new MetricsMiddleware(metrics);
        await mw.InvokeAsync(MakeCtx(), () => Task.CompletedTask);
        Assert.Equal("ok", metrics.Counters[0].Tags!["outcome"]);
    }

    [Fact]
    public async Task InvokeAsync_Success_CounterTagsContainModule()
    {
        var metrics = new FakeMetrics();
        var mw = new MetricsMiddleware(metrics);
        await mw.InvokeAsync(MakeCtx("horse"), () => Task.CompletedTask);
        Assert.Equal("horse", metrics.Counters[0].Tags!["module"]);
    }

    [Fact]
    public async Task InvokeAsync_Success_CounterTagsContainCommandName()
    {
        var metrics = new FakeMetrics();
        var mw = new MetricsMiddleware(metrics);
        await mw.InvokeAsync(MakeCtx(), () => Task.CompletedTask);
        Assert.Equal(nameof(FakeCommand), metrics.Counters[0].Tags!["command"]);
    }

    [Fact]
    public async Task InvokeAsync_HandlerThrows_CounterOutcomeIsError()
    {
        var metrics = new FakeMetrics();
        var mw = new MetricsMiddleware(metrics);
        try { await mw.InvokeAsync(MakeCtx(), () => throw new InvalidOperationException()); }
        catch (InvalidOperationException) { }
        Assert.Equal("error", metrics.Counters[0].Tags!["outcome"]);
    }

    [Fact]
    public async Task InvokeAsync_HandlerThrows_StillRethrows()
    {
        var metrics = new FakeMetrics();
        var mw = new MetricsMiddleware(metrics);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            mw.InvokeAsync(MakeCtx(), () => throw new InvalidOperationException("test")));
    }

    // ── Histogram ────────────────────────────────────────────────────────────

    [Fact]
    public async Task InvokeAsync_Success_ObservesHistogram()
    {
        var metrics = new FakeMetrics();
        var mw = new MetricsMiddleware(metrics);
        await mw.InvokeAsync(MakeCtx(), () => Task.CompletedTask);
        Assert.Single(metrics.Histograms);
        Assert.Equal("bot_command_duration_ms", metrics.Histograms[0].Name);
    }

    [Fact]
    public async Task InvokeAsync_Success_HistogramValueIsNonNegative()
    {
        var metrics = new FakeMetrics();
        var mw = new MetricsMiddleware(metrics);
        await mw.InvokeAsync(MakeCtx(), () => Task.CompletedTask);
        Assert.True(metrics.Histograms[0].Value >= 0);
    }

    [Fact]
    public async Task InvokeAsync_HandlerThrows_HistogramStillObserved()
    {
        var metrics = new FakeMetrics();
        var mw = new MetricsMiddleware(metrics);
        try { await mw.InvokeAsync(MakeCtx(), () => throw new InvalidOperationException()); }
        catch (InvalidOperationException) { }
        Assert.Single(metrics.Histograms);
    }

    // ── CallsNext ────────────────────────────────────────────────────────────

    [Fact]
    public async Task InvokeAsync_CallsNext()
    {
        var metrics = new FakeMetrics();
        var mw = new MetricsMiddleware(metrics);
        var called = false;
        await mw.InvokeAsync(MakeCtx(), () => { called = true; return Task.CompletedTask; });
        Assert.True(called);
    }
}

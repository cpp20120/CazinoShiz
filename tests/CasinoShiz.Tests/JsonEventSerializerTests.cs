using BotFramework.Host;
using BotFramework.Host.Composition;
using BotFramework.Sdk;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CasinoShiz.Tests;

public class JsonEventSerializerTests
{
    // ── Minimal IDomainEvent for serialization tests ──────────────────────────

    public sealed record TestEvent(string Name, int Value) : IDomainEvent
    {
        public string EventType => "test.event";
        public long OccurredAt { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    // ── Module whose assembly contains TestEvent ─────────────────────────────

    private sealed class TestModule : IModule
    {
        public string Id => "test";
        public string DisplayName => "Test";
        public string Version => "1.0";
        public void ConfigureServices(IModuleServiceCollection services) { }
        public IReadOnlyList<LocaleBundle> GetLocales() => [];
    }

    private static JsonEventSerializer MakeSerializer(IModule? module = null)
    {
        var modules = module == null ? Array.Empty<IModule>() : new[] { module };
        var loaded = new LoadedModules(modules, new Dictionary<string, Dictionary<string, string>>(), [], []);
        return new JsonEventSerializer(loaded, NullLogger<JsonEventSerializer>.Instance);
    }

    // ── Serialize ─────────────────────────────────────────────────────────────

    [Fact]
    public void Serialize_ProducesJson()
    {
        var serializer = MakeSerializer();
        var ev = new TestEvent("hello", 42);
        var json = serializer.Serialize(ev);
        Assert.NotEmpty(json);
        Assert.StartsWith("{", json);
    }

    [Fact]
    public void Serialize_ContainsFieldValues()
    {
        var serializer = MakeSerializer();
        var ev = new TestEvent("world", 99);
        var json = serializer.Serialize(ev);
        Assert.Contains("world", json);
        Assert.Contains("99", json);
    }

    [Fact]
    public void Serialize_RoundTrips_ViaDeserialize()
    {
        var serializer = MakeSerializer(new TestModule());
        var original = new TestEvent("test-name", 123);
        var json = serializer.Serialize(original);
        var roundTripped = serializer.Deserialize("test.event", json) as TestEvent;

        Assert.NotNull(roundTripped);
        Assert.Equal("test-name", roundTripped!.Name);
        Assert.Equal(123, roundTripped.Value);
    }

    // ── Deserialize ───────────────────────────────────────────────────────────

    [Fact]
    public void Deserialize_UnknownEventType_ReturnsNull()
    {
        var serializer = MakeSerializer();
        var result = serializer.Deserialize("unknown.event_type", "{}");
        Assert.Null(result);
    }

    [Fact]
    public void Deserialize_KnownEventType_ReturnsEvent()
    {
        var serializer = MakeSerializer(new TestModule());
        var json = """{"Name":"foo","Value":7,"EventType":"test.event","OccurredAt":0}""";
        var result = serializer.Deserialize("test.event", json);
        Assert.NotNull(result);
        Assert.IsType<TestEvent>(result);
    }

    [Fact]
    public void Deserialize_KnownEventType_CorrectEventType()
    {
        var serializer = MakeSerializer(new TestModule());
        var json = """{"Name":"x","Value":1,"EventType":"test.event","OccurredAt":0}""";
        var result = serializer.Deserialize("test.event", json);
        Assert.Equal("test.event", result!.EventType);
    }

    // ── Type table construction ───────────────────────────────────────────────

    [Fact]
    public void Constructor_NoModules_DoesNotThrow()
    {
        var ex = Record.Exception(() => MakeSerializer());
        Assert.Null(ex);
    }

    [Fact]
    public void Constructor_WithModule_FindsEventTypes()
    {
        var serializer = MakeSerializer(new TestModule());
        // If the type table was built correctly, Deserialize should work
        var json = """{"Name":"a","Value":0,"EventType":"test.event","OccurredAt":0}""";
        var result = serializer.Deserialize("test.event", json);
        Assert.NotNull(result);
    }

    [Fact]
    public void Constructor_SameAssemblyRegisteredTwice_OnlyProcessedOnce()
    {
        // Two modules from the same assembly — should not throw on duplicate scanning
        var loaded = new LoadedModules(
            [new TestModule(), new TestModule()],
            new Dictionary<string, Dictionary<string, string>>(), [], []);
        var ex = Record.Exception(() => new JsonEventSerializer(loaded, NullLogger<JsonEventSerializer>.Instance));
        Assert.Null(ex);
    }
}

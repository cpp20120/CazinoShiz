using BotFramework.Host;
using BotFramework.Sdk;
using Xunit;

namespace CasinoShiz.Tests;

public class InProcessEventBusTests
{
    private sealed class FakeEvent(string eventType) : IDomainEvent
    {
        public string EventType { get; } = eventType;
        public long OccurredAt { get; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    private sealed class RecordingSubscriber : IDomainEventSubscriber
    {
        public List<IDomainEvent> Received { get; } = [];
        public Task HandleAsync(IDomainEvent ev, CancellationToken ct) { Received.Add(ev); return Task.CompletedTask; }
    }

    // ── Pattern matching ─────────────────────────────────────────────────────

    [Fact]
    public async Task Publish_ExactMatch_Dispatches()
    {
        var bus = new InProcessEventBus();
        var sub = new RecordingSubscriber();
        bus.Subscribe("sh.game_ended", sub);

        await bus.PublishAsync(new FakeEvent("sh.game_ended"), default);

        Assert.Single(sub.Received);
    }

    [Fact]
    public async Task Publish_ExactMatch_NoMatchOnDifferentEvent()
    {
        var bus = new InProcessEventBus();
        var sub = new RecordingSubscriber();
        bus.Subscribe("sh.game_ended", sub);

        await bus.PublishAsync(new FakeEvent("sh.player_joined"), default);

        Assert.Empty(sub.Received);
    }

    [Fact]
    public async Task Publish_WildcardStar_MatchesAll()
    {
        var bus = new InProcessEventBus();
        var sub = new RecordingSubscriber();
        bus.Subscribe("*", sub);

        await bus.PublishAsync(new FakeEvent("sh.game_ended"), default);
        await bus.PublishAsync(new FakeEvent("poker.hand_started"), default);

        Assert.Equal(2, sub.Received.Count);
    }

    [Fact]
    public async Task Publish_ModuleWildcard_MatchesAllActionsForModule()
    {
        var bus = new InProcessEventBus();
        var sub = new RecordingSubscriber();
        bus.Subscribe("sh.*", sub);

        await bus.PublishAsync(new FakeEvent("sh.game_ended"), default);
        await bus.PublishAsync(new FakeEvent("sh.player_joined"), default);
        await bus.PublishAsync(new FakeEvent("poker.hand_started"), default);

        Assert.Equal(2, sub.Received.Count);
    }

    [Fact]
    public async Task Publish_ActionWildcard_MatchesSameActionAcrossModules()
    {
        var bus = new InProcessEventBus();
        var sub = new RecordingSubscriber();
        bus.Subscribe("*.game_ended", sub);

        await bus.PublishAsync(new FakeEvent("sh.game_ended"), default);
        await bus.PublishAsync(new FakeEvent("poker.game_ended"), default);
        await bus.PublishAsync(new FakeEvent("sh.player_joined"), default);

        Assert.Equal(2, sub.Received.Count);
    }

    [Fact]
    public async Task Publish_ModuleWildcard_NoMatchForOtherModule()
    {
        var bus = new InProcessEventBus();
        var sub = new RecordingSubscriber();
        bus.Subscribe("sh.*", sub);

        await bus.PublishAsync(new FakeEvent("poker.game_ended"), default);

        Assert.Empty(sub.Received);
    }

    [Fact]
    public async Task Publish_NoDotInEvent_NoMatchForPatternWithDot()
    {
        var bus = new InProcessEventBus();
        var sub = new RecordingSubscriber();
        bus.Subscribe("sh.event", sub);

        await bus.PublishAsync(new FakeEvent("nodot"), default);

        Assert.Empty(sub.Received);
    }

    [Fact]
    public async Task Publish_NoDotInPattern_NoMatch()
    {
        var bus = new InProcessEventBus();
        var sub = new RecordingSubscriber();
        bus.Subscribe("nodotpattern", sub);

        await bus.PublishAsync(new FakeEvent("sh.game_ended"), default);

        Assert.Empty(sub.Received);
    }

    // ── Multiple subscribers ─────────────────────────────────────────────────

    [Fact]
    public async Task Publish_MultipleSubscribersMatch_AllDispatched()
    {
        var bus = new InProcessEventBus();
        var sub1 = new RecordingSubscriber();
        var sub2 = new RecordingSubscriber();
        bus.Subscribe("sh.game_ended", sub1);
        bus.Subscribe("*", sub2);

        await bus.PublishAsync(new FakeEvent("sh.game_ended"), default);

        Assert.Single(sub1.Received);
        Assert.Single(sub2.Received);
    }

    [Fact]
    public async Task Publish_NoSubscribers_DoesNotThrow()
    {
        var bus = new InProcessEventBus();
        var ex = await Record.ExceptionAsync(() => bus.PublishAsync(new FakeEvent("sh.game_ended"), default));
        Assert.Null(ex);
    }

    [Fact]
    public async Task Publish_SubscriberReceivesCorrectEvent()
    {
        var bus = new InProcessEventBus();
        var sub = new RecordingSubscriber();
        bus.Subscribe("sh.game_ended", sub);

        var ev = new FakeEvent("sh.game_ended");
        await bus.PublishAsync(ev, default);

        Assert.Same(ev, sub.Received[0]);
    }
}

using BotFramework.Host.Execution;
using BotFramework.Narrative;
using BotFramework.Narrative.Host;
using BotFramework.Sdk.Execution;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CasinoShiz.Tests;

public sealed class NarrativeEffectSystemTests
{
    private static readonly IReadOnlyDictionary<Type, IGameRecordWriter> EmptyWriters =
        new Dictionary<Type, IGameRecordWriter>();

    [Fact]
    public void NarrativeContracts_AreSemanticAndValidateChoiceInput()
    {
        var address = new NarrativeAddress("table:42", "player:7");
        var effect = new ChoiceEffect(
            address,
            "round:42:choice",
            [
                new NarrativeChoiceOption("draw", new NarrativeText("choice.draw"), "draw-card"),
                new NarrativeChoiceOption("stand", new NarrativeText("choice.stand"), "stand"),
            ]);
        var selection = new NarrativeChoiceSelection("round:42:choice", "draw", "draw-card");

        Assert.Equal(NarrativeCapabilities.Choices, effect.RequiredCapability);
        Assert.Equal("table:42", effect.Target.ConversationId);
        Assert.Equal("draw", selection.OptionId);
        Assert.Throws<ArgumentException>(() => new ChoiceEffect(
            address,
            "duplicate-options",
            [
                new NarrativeChoiceOption("same", new NarrativeText("choice.one")),
                new NarrativeChoiceOption("same", new NarrativeText("choice.two")),
            ]));
    }

    [Fact]
    public async Task NarrativeSink_ReceivesAllEffectsThroughTheGenericHostPipeline()
    {
        var services = new ServiceCollection();
        services.AddNarrativeEffectSink<RecordingNarrativeSink>();
        services.AddSingleton<INarrativeProjectionWriter, RecordingNarrativeProjectionWriter>();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var scoped = scope.ServiceProvider;
        var runtimeProviders = scoped.GetServices<IGameRuntimeCapabilityProvider>();
        var validator = new GameRuntimeCapabilityValidator(runtimeProviders);
        var handlers = scoped.GetServices<IGameEffectHandler>()
            .ToDictionary(handler => handler.EffectType);

        var address = new NarrativeAddress("story:42", "player:7");
        var effects = new NarrativeEffect[]
        {
            new SceneEffect(address, "forest", new NarrativeText("scene.forest.title")),
            new DialogEffect(address, new NarrativeText("guide.hello"), "guide"),
            new ChoiceEffect(address, "forest:turn:1", [new NarrativeChoiceOption("follow", new NarrativeText("choice.follow"))]),
            new NarrativeFlagEffect(address, "met-guide"),
            new NarrativeCheckpointEffect(address, "forest:arrival", "resume:42"),
        };
        var decision = new GameDecision<object, string>(
            DecisionStatus.Accepted,
            new object(),
            "ok",
            [],
            [],
            [],
            [],
            [],
            CustomEffects: effects);
        var requiredCapabilities = new GameCapabilitySet(
        [
            NarrativeCapabilities.Scenes,
            NarrativeCapabilities.Dialog,
            NarrativeCapabilities.Choices,
            NarrativeCapabilities.Flags,
            NarrativeCapabilities.Checkpoints,
        ]);

        var plan = GameEffectPlan.Create(
            decision,
            [],
            EmptyWriters,
            handlers,
            requiredCapabilities,
            validator);

        foreach (var (handler, batch) in plan.Custom)
            await handler.ApplyAsync(batch, new TestExecutionContext(), CancellationToken.None);

        var sink = Assert.IsType<RecordingNarrativeSink>(
            scoped.GetRequiredService<INarrativeEffectSink>());
        Assert.Equal(effects, sink.Effects);
        Assert.Equal("operation:42", sink.Context?.OperationId);
        Assert.Equal("game-effect:42", sink.Context?.DeliveryId);
        var writer = Assert.IsType<RecordingNarrativeProjectionWriter>(
            scoped.GetRequiredService<INarrativeProjectionWriter>());
        Assert.Equal(effects, writer.Effects);
        Assert.Equal("game-effect:42", writer.Context?.DeliveryId);
        Assert.Contains(runtimeProviders, provider => provider.Capabilities.Contains(NarrativeCapabilities.Scenes));
        Assert.Contains(runtimeProviders, provider => provider.Capabilities.Contains(NarrativeCapabilities.Checkpoints));
    }

    [Fact]
    public void NarrativeEffects_CannotBeEmittedWithoutManifestDeclaration()
    {
        var effect = new DialogEffect(
            new NarrativeAddress("story:42", "player:7"),
            new NarrativeText("guide.hello"));
        var decision = new GameDecision<object, string>(
            DecisionStatus.Accepted,
            new object(),
            "ok",
            [],
            [],
            [],
            [],
            [],
            CustomEffects: [effect]);
        var error = Assert.Throws<InvalidOperationException>(() => GameEffectPlan.Create(
            decision,
            [],
            EmptyWriters,
            new Dictionary<Type, IGameEffectHandler> { [typeof(DialogEffect)] = new IgnoredDialogHandler() },
            new GameCapabilitySet([NarrativeCapabilities.Scenes]),
            new GameRuntimeCapabilityValidator(
                [new NarrativeRuntimeCapabilityProvider()])));

        Assert.Contains(NarrativeCapabilities.Dialog.Id, error.Message, StringComparison.Ordinal);
    }

    private sealed class RecordingNarrativeSink : INarrativeEffectSink
    {
        public List<NarrativeEffect> Effects { get; } = [];

        public NarrativeDeliveryContext? Context { get; private set; }

        public Task ApplyAsync(
            IReadOnlyList<NarrativeEffect> effects,
            NarrativeDeliveryContext context,
            CancellationToken ct)
        {
            Effects.AddRange(effects);
            Context = context;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingNarrativeProjectionWriter : INarrativeProjectionWriter
    {
        public List<NarrativeEffect> Effects { get; } = [];

        public NarrativeDeliveryContext? Context { get; private set; }

        public Task ApplyAsync(NarrativeEffect effect, NarrativeDeliveryContext context, CancellationToken ct)
        {
            Effects.Add(effect);
            Context = context;
            return Task.CompletedTask;
        }
    }

    private sealed class TestExecutionContext : IGameExecutionContext
    {
        public string? OperationId => "operation:42";

        public string? EffectDeliveryId => "game-effect:42";

        public Task<int> ExecuteAsync(string sql, object? parameters, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<T?> QuerySingleOrDefaultAsync<T>(string sql, object? parameters, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    private sealed class IgnoredDialogHandler : GameEffectHandler<DialogEffect>
    {
        protected override Task ApplyBatchAsync(
            IReadOnlyList<DialogEffect> effects,
            IGameExecutionContext context,
            CancellationToken ct) => Task.CompletedTask;
    }
}

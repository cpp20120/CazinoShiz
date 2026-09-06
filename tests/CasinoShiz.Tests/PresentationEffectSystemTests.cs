using BotFramework.Host.Execution;
using BotFramework.Presentation;
using BotFramework.Presentation.Host;
using BotFramework.Sdk.Execution;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using Xunit;

namespace CasinoShiz.Tests;

public sealed class PresentationEffectSystemTests
{
    private static readonly IReadOnlyDictionary<Type, IGameRecordWriter> EmptyWriters =
        new Dictionary<Type, IGameRecordWriter>();

    [Fact]
    public void PresentationContracts_AreSemanticAndValidateStructuredInput()
    {
        var address = new PresentationAddress("table:42", "player:7");
        var request = new InputRequestEffect(
            "bet:42",
            "player:7",
            "table:42",
            "table.bet",
            DateTimeOffset.UtcNow.AddMinutes(1));
        var form = new InputFormEffect(
            address,
            request.RequestId,
            new PresentationText("bet.title"),
            [new PresentationInputField("amount", new PresentationText("bet.amount"), PresentationInputKind.Number)]);
        var media = new MediaEffect(
            address,
            new PresentationMedia(PresentationMediaKind.Image, "render:table:42", "image/png"));

        Assert.Equal(request.RequestId, form.RequestId);
        Assert.Equal(MessagingCapabilities.Input, form.RequiredCapability);
        Assert.Equal(MessagingCapabilities.Media, media.RequiredCapability);
        Assert.Throws<ArgumentException>(() => new InputFormEffect(
            address,
            request.RequestId,
            new PresentationText("bet.title"),
            [
                new PresentationInputField("amount", new PresentationText("bet.amount")),
                new PresentationInputField("amount", new PresentationText("bet.amount-again")),
            ]));
    }

    [Fact]
    public async Task PresentationSink_ReceivesDurableEffectsThroughTheGenericHostPipeline()
    {
        var services = new ServiceCollection();
        services.AddPresentationEffectSink<RecordingPresentationSink>();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var scoped = scope.ServiceProvider;
        var runtimeProviders = scoped.GetServices<IGameRuntimeCapabilityProvider>();
        var validator = new GameRuntimeCapabilityValidator(runtimeProviders);
        var handlers = scoped.GetServices<IGameEffectHandler>()
            .ToDictionary(handler => handler.EffectType);

        var address = new PresentationAddress("table:42", "player:7");
        var effects = new PresentationEffect[]
        {
            new NotificationEffect(address, new PresentationText("table.ready"), PresentationTone.Success),
            new RichResultEffect(
                address,
                "round:42",
                new PresentationText("round.result"),
                fields:
                [new PresentationField("payout", new PresentationText("round.payout"), new PresentationText("money", fallback: "100"))]),
            new InputFormEffect(
                address,
                "bet:42",
                new PresentationText("bet.title"),
                [new PresentationInputField("amount", new PresentationText("bet.amount"), PresentationInputKind.Number)]),
            new MediaEffect(
                address,
                new PresentationMedia(PresentationMediaKind.Image, "render:round:42", "image/png")),
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
            MessagingCapabilities.Messages,
            MessagingCapabilities.RichResults,
            MessagingCapabilities.Input,
            MessagingCapabilities.Media,
        ]);

        var plan = GameEffectPlan.Create(
            decision,
            [],
            EmptyWriters,
            handlers,
            requiredCapabilities,
            validator);

        Assert.Equal(4, plan.DurableEffects.Count);
        Assert.All(effects, static effect => Assert.IsAssignableFrom<IDurableGameEffect>(effect));
        foreach (var (handler, batch) in plan.Custom)
            await handler.ApplyAsync(batch, new TestExecutionContext(), CancellationToken.None);

        var sink = Assert.IsType<RecordingPresentationSink>(
            scoped.GetRequiredService<IPresentationEffectSink>());
        Assert.Equal(effects, sink.Effects);
        Assert.Equal("operation:42", sink.Context?.OperationId);
        Assert.Equal("game-effect:42", sink.Context?.DeliveryId);
        Assert.Contains(runtimeProviders, provider => provider.Capabilities.Contains(MessagingCapabilities.RichResults));
        Assert.Contains(runtimeProviders, provider => provider.Capabilities.Contains(MessagingCapabilities.Media));
    }

    [Fact]
    public void PresentationEffects_RoundTripThroughTheDurableOutboxSerializer()
    {
        var address = new PresentationAddress("table:42", "player:7");
        PresentationEffect[] effects =
        [
            new NotificationEffect(address, new PresentationText("table.ready")),
            new RichResultEffect(address, "round:42", new PresentationText("round.result")),
            new InputFormEffect(
                address,
                "bet:42",
                new PresentationText("bet.title"),
                [new PresentationInputField("amount", new PresentationText("bet.amount"), PresentationInputKind.Number)]),
            new MediaEffect(address, new PresentationMedia(PresentationMediaKind.Image, "render:round:42")),
        ];
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        foreach (var effect in effects)
        {
            var type = effect.GetType();
            var payload = JsonSerializer.Serialize(effect, type, options);
            var restored = JsonSerializer.Deserialize(payload, type, options);

            Assert.NotNull(restored);
            Assert.Equal(type, restored.GetType());
            Assert.IsAssignableFrom<IDurableGameEffect>(restored);
        }
    }

    [Fact]
    public void MediaEffects_CannotBeEmittedWithoutManifestDeclaration()
    {
        var effect = new MediaEffect(
            new PresentationAddress("table:42", "player:7"),
            new PresentationMedia(PresentationMediaKind.Image, "render:42"));
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
            new Dictionary<Type, IGameEffectHandler>
            {
                [typeof(PresentationEffect)] = new IgnoredPresentationHandler(),
            },
            new GameCapabilitySet([MessagingCapabilities.Messages]),
            new GameRuntimeCapabilityValidator([new PresentationRuntimeCapabilityProvider()])));

        Assert.Contains(MessagingCapabilities.Media.Id, error.Message, StringComparison.Ordinal);
    }

    private sealed class RecordingPresentationSink : IPresentationEffectSink
    {
        public List<PresentationEffect> Effects { get; } = [];

        public PresentationDeliveryContext? Context { get; private set; }

        public Task ApplyAsync(
            IReadOnlyList<PresentationEffect> effects,
            PresentationDeliveryContext context,
            CancellationToken ct)
        {
            Effects.AddRange(effects);
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

    private sealed class IgnoredPresentationHandler : GameEffectHandler<PresentationEffect>
    {
        protected override Task ApplyBatchAsync(
            IReadOnlyList<PresentationEffect> effects,
            IGameExecutionContext context,
            CancellationToken ct) => Task.CompletedTask;
    }
}

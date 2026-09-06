using BotFramework.Host.Execution;
using BotFramework.Narrative;
using BotFramework.Sdk.Execution;
using Xunit;

namespace CasinoShiz.Tests;

public sealed class GameInteractionAndEffectOutboxTests
{
    private static readonly IReadOnlyDictionary<Type, IGameRecordWriter> EmptyWriters =
        new Dictionary<Type, IGameRecordWriter>();

    [Fact]
    public void InputRequest_BindsPlayerScopeValueAndExpiryWithoutTransportTypes()
    {
        var effect = new InputRequestEffect(
            "forest:choice:42",
            "player:7",
            "story:42",
            "story.choice",
            DateTimeOffset.UtcNow.AddMinutes(5),
            """{"chapter":"forest"}""",
            ["follow", "leave"],
            "session:42");

        Assert.Equal(MessagingCapabilities.Input, effect.RequiredCapability);
        Assert.Equal("player:7", effect.ExpectedPlayerId);
        Assert.Equal(["follow", "leave"], effect.AllowedValues);
        Assert.Throws<ArgumentException>(() => new InputRequestEffect(
            "bad",
            "player:7",
            "story:42",
            "story.choice",
            DateTimeOffset.UtcNow,
            allowedValues: ["same", "same"]));
    }

    [Fact]
    public async Task InputDispatcher_UsesValidatedRequestAndCorrelationForTheGameRoute()
    {
        var request = Request();
        var service = new FakeInputRequestService(
            request,
            new(GameInputRequestConsumeStatus.Accepted, request));
        var handler = new RecordingInputRouteHandler();
        var dispatcher = new GameInputRequestDispatcher(service, [handler]);
        var submission = new GameInputSubmission(
            request.RequestId,
            "player:7",
            "story:42",
            "follow",
            "input-delivery:42");

        var result = await dispatcher.DispatchAsync(submission, CancellationToken.None);

        Assert.Equal(GameInputRequestDispatchStatus.Dispatched, result.Status);
        Assert.Same(request, handler.Request);
        Assert.Same(submission, handler.Submission);
        Assert.Equal("input-delivery:42", handler.Submission?.CorrelationId);
    }

    [Fact]
    public void EffectPlan_SeparatesDurableNarrativeEffectsFromTransactionalInputRequests()
    {
        var input = new InputRequestEffect(
            "forest:choice:42",
            "player:7",
            "story:42",
            "story.choice",
            DateTimeOffset.UtcNow.AddMinutes(5),
            allowedValues: ["follow"]);
        var scene = new SceneEffect(
            new NarrativeAddress("story:42", "player:7"),
            "forest",
            new NarrativeText("scene.forest.title"));
        var decision = new GameDecision<object, string>(
            DecisionStatus.Accepted,
            new object(),
            "ok",
            [],
            [],
            [],
            [],
            [],
            CustomEffects: [input, scene]);
        var handlers = new Dictionary<Type, IGameEffectHandler>
        {
            [typeof(InputRequestEffect)] = new IgnoreInputRequestHandler(),
            [typeof(NarrativeEffect)] = new IgnoreNarrativeHandler(),
        };
        var requiredCapabilities = new GameCapabilitySet(
            [MessagingCapabilities.Input, NarrativeCapabilities.Scenes]);
        var validator = new GameRuntimeCapabilityValidator(
        [
            new StaticGameRuntimeCapabilityProvider(
                [MessagingCapabilities.Input, NarrativeCapabilities.Scenes]),
        ]);

        var plan = GameEffectPlan.Create(
            decision,
            [],
            EmptyWriters,
            handlers,
            requiredCapabilities,
            validator);

        var durable = Assert.Single(plan.DurableEffects);
        Assert.Equal(1, durable.EffectIndex);
        Assert.Same(scene, durable.Effect);
        Assert.Contains(plan.Custom, batch => batch.Effects.Contains(input));
        Assert.Contains(plan.Custom, batch => batch.Effects.Contains(scene));
    }

    private static GameInputRequest Request() => new(
        "forest:choice:42",
        "story-game",
        "story:42",
        "player:7",
        "story:42",
        "story.choice",
        "{}",
        ["follow", "leave"],
        "session:42",
        DateTimeOffset.UtcNow.AddMinutes(5),
        GameInputRequestStatus.Pending,
        null,
        null,
        DateTimeOffset.UtcNow,
        null);

    private sealed class FakeInputRequestService(
        GameInputRequest? request,
        GameInputRequestConsumeResult result) : IGameInputRequestService
    {
        public Task<GameInputRequest?> GetAsync(string requestId, CancellationToken ct) =>
            Task.FromResult(request);

        public Task<GameInputRequestConsumeResult> ConsumeAsync(GameInputSubmission submission, CancellationToken ct) =>
            Task.FromResult(result);
    }

    private sealed class RecordingInputRouteHandler : IGameInputRouteHandler
    {
        public string GameId => "story-game";

        public string Route => "story.choice";

        public GameInputRequest? Request { get; private set; }

        public GameInputSubmission? Submission { get; private set; }

        public Task HandleAsync(
            GameInputRequest request,
            GameInputSubmission submission,
            CancellationToken ct)
        {
            Request = request;
            Submission = submission;
            return Task.CompletedTask;
        }
    }

    private sealed class IgnoreInputRequestHandler : GameEffectHandler<InputRequestEffect>
    {
        protected override Task ApplyBatchAsync(
            IReadOnlyList<InputRequestEffect> effects,
            IGameExecutionContext context,
            CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class IgnoreNarrativeHandler : GameEffectHandler<NarrativeEffect>
    {
        protected override Task ApplyBatchAsync(
            IReadOnlyList<NarrativeEffect> effects,
            IGameExecutionContext context,
            CancellationToken ct) => Task.CompletedTask;
    }
}

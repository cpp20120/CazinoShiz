using BotFramework.Host.Execution;
using BotFramework.Sdk.Execution;
using Xunit;

namespace CasinoShiz.Tests;

public sealed class GameCapabilitySystemTests
{
    private static readonly IReadOnlyDictionary<Type, IGameRecordWriter> EmptyWriters =
        new Dictionary<Type, IGameRecordWriter>();

    [Fact]
    public void Manifest_SeparatesExecutionStyleFromRuntimeRequirements()
    {
        var definition = new GameDefinition(
            "capability-test",
            "Capability test",
            GameCapabilities.TurnBased,
            requiredCapabilities:
            [
                TurnBasedCapabilities.Turns,
                SchedulingCapabilities.Timers,
                MessagingCapabilities.Input,
            ]);

        Assert.Equal(GameCapabilities.TurnBased, definition.ExecutionStyles);
        Assert.True(definition.RequiredCapabilities.Contains(TurnBasedCapabilities.Turns));
        Assert.True(definition.RequiredCapabilities.Contains(SchedulingCapabilities.Timers));
        Assert.True(definition.RequiredCapabilities.Contains(MessagingCapabilities.Input));
    }

    [Fact]
    public void EffectPlan_AcceptsEffectWhenGameDeclaredAndRuntimeProvidesItsCapability()
    {
        var plan = GameEffectPlan.Create(
            EconomyDecision(),
            [],
            EmptyWriters,
            requiredCapabilities: new([EconomyCapabilities.Wallet]),
            capabilityValidator: Validator(EconomyCapabilities.Wallet));

        Assert.Single(plan.Effects.Economy);
    }

    [Fact]
    public void EffectPlan_RejectsEffectThatWasNotDeclaredByTheGame()
    {
        var error = Assert.Throws<InvalidOperationException>(() => GameEffectPlan.Create(
            EconomyDecision(),
            [],
            EmptyWriters,
            requiredCapabilities: new([SchedulingCapabilities.Timers]),
            capabilityValidator: Validator(EconomyCapabilities.Wallet, SchedulingCapabilities.Timers)));

        Assert.Contains(EconomyCapabilities.Wallet.Id, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validator_RejectsUnavailableFrontendCapability()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Validator(MessagingCapabilities.Messages).Validate(
            new([NarrativeCapabilities.Scenes]),
            EmptyEffects()));

        Assert.Contains(NarrativeCapabilities.Scenes.Id, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validator_AllowsAdapterDefinedCapabilityEffect()
    {
        var effect = new SemanticMessageEffect();

        Validator(MessagingCapabilities.Messages).Validate(
            new([MessagingCapabilities.Messages]),
            new([], [], [], [effect], [], []));
    }

    [Fact]
    public void Validator_RequiresCustomEffectsToNameTheirCapability()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Validator(MessagingCapabilities.Messages).Validate(
            new([MessagingCapabilities.Messages]),
            new([], [], [], [new UnboundEffect()], [], [])));

        Assert.Contains(nameof(IGameCapabilityEffect), error.Message, StringComparison.Ordinal);
    }

    private static IGameCapabilityValidator Validator(params GameCapability[] capabilities) =>
        new GameRuntimeCapabilityValidator([new StaticGameRuntimeCapabilityProvider(capabilities)]);

    private static GameDecision<object, string> EconomyDecision() => new(
        DecisionStatus.Accepted,
        new object(),
        "ok",
        [EconomyEffect.Debit(10, "test")],
        [],
        [],
        [],
        []);

    private static GameEffectSet EmptyEffects() => new([], [], [], [], [], []);

    private sealed record SemanticMessageEffect : IGameCapabilityEffect
    {
        public GameCapability RequiredCapability => MessagingCapabilities.Messages;
    }

    private sealed record UnboundEffect : IGameEffect;
}

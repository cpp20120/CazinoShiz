using BotFramework.Host.Execution;
using BotFramework.Host.Execution.InstantWager;
using BotFramework.Sdk.Events.Meta;
using BotFramework.Sdk.Execution;
using BotFramework.Sdk.Execution.InstantWager;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CasinoShiz.Tests;

public sealed class GameFrameworkPrimitiveTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CompletionFactory_NormalizesStakePayoutAndPlayer()
    {
        var completion = GameCompletionEventFactory.Create(new(
            new GameCommandContext(10, "alice", 20, "command"),
            "test-game",
            25,
            50,
            Now));

        Assert.Equal((20L, 10L, "alice", "test-game"),
            (completion.ChatId, completion.UserId, completion.DisplayName, completion.GameKey));
        Assert.True(completion.IsWin);
        Assert.Equal(2m, completion.Multiplier);
        Assert.Equal(Now.ToUnixTimeMilliseconds(), completion.OccurredAt);
    }

    [Fact]
    public void PlayerDescriptor_MapsTheSharedCommandContext()
    {
        var game = new GameDefinition("context-game", "Context game");
        var descriptor = new ContextDescriptor(game);
        var command = new ContextCommand(new(10, "alice", 20, "command"));

        Assert.Equal("context-game", descriptor.GameId);
        Assert.Equal("command", descriptor.CommandId(command));
        Assert.Equal("context-game:20:10", descriptor.AggregateId(command));
        Assert.Equal(new WalletIdentity(10, 20), descriptor.Wallet(command));
    }

    [Fact]
    public void InstantWager_ResolvesEffectsQuotaAndCompletionEvent()
    {
        var definition = InstantDefinition();
        var action = new InstantWagerAction<InstantGame, CoinSide>(definition);
        var input = new GameActionInput<NoGameState, InstantWagerCommand<InstantGame>>(
            new(new(10, "alice", 20, "command"), 25),
            default,
            new WalletSnapshot(100),
            new Dictionary<string, QuotaSnapshot> { ["instant.daily"] = new(1, 5) },
            new([KeyValuePair.Create("roll", 0.25)]),
            Now);

        var decision = action.Decide(input);

        Assert.Equal(DecisionStatus.Accepted, decision.Status);
        Assert.Equal((InstantWagerStatus.Accepted, CoinSide.Heads, 25L, 50L, 125L),
            (decision.Result.Status, decision.Result.Outcome, decision.Result.Stake, decision.Result.Payout, decision.Result.Balance));
        Assert.Equal(new InstantWagerQuotaUsage(2, 5), decision.Result.Quota);
        Assert.Equal(
            [EconomyEffect.Debit(25, "instant-test.bet"), EconomyEffect.Credit(50, "instant-test.payout")],
            decision.Economy);
        Assert.Equal(QuotaEffect.Consume("instant.daily"), Assert.Single(decision.Quotas));
        var completion = Assert.IsType<GameCompletedMetaEvent>(Assert.Single(decision.Events));
        Assert.Equal(("instant-test", 25L, 50L, true),
            (completion.GameKey, completion.Stake, completion.Payout, completion.IsWin));
    }

    [Fact]
    public void InstantWager_RejectsInvalidAmountBeforeResolvingOutcome()
    {
        var action = new InstantWagerAction<InstantGame, CoinSide>(InstantDefinition());
        var input = new GameActionInput<NoGameState, InstantWagerCommand<InstantGame>>(
            new(new(10, "alice", 20, "command"), 5),
            default,
            new WalletSnapshot(100),
            new Dictionary<string, QuotaSnapshot> { ["instant.daily"] = new(0, 5) },
            EntropyValue.Empty,
            Now);

        var decision = action.Decide(input);

        Assert.Equal(DecisionStatus.Rejected, decision.Status);
        Assert.Equal(InstantWagerStatus.InvalidAmount, decision.Result.Status);
        Assert.Empty(decision.EffectSet.MaterializeEffects());
    }

    [Fact]
    public void Registration_ExposesManifestAndStatelessPipeline()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IGameDailyQuotaPolicy, EmptyQuotaPolicy>();
        services.AddInstantWagerGame(InstantDefinition());

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var scoped = scope.ServiceProvider;

        var catalog = provider.GetRequiredService<IGameCatalog>();
        Assert.Equal("Instant test", catalog.GetRequired("instant-test").DisplayName);
        Assert.IsType<InstantWagerAction<InstantGame, CoinSide>>(
            scoped.GetRequiredService<IGameAction<
                InstantWagerCommand<InstantGame>,
                NoGameState,
                InstantWagerResult<CoinSide>>>());
        Assert.IsType<StatelessGameStateStore<InstantWagerCommand<InstantGame>>>(
            scoped.GetRequiredService<IGameStateStore<InstantWagerCommand<InstantGame>, NoGameState>>());
    }

    private static InstantWagerDefinition<InstantGame, CoinSide> InstantDefinition() => new(
        new GameDefinition(
            "instant-test",
            "Instant test",
            GameCapabilities.InstantWager,
            new GameStakeLimits(10, 100),
            "instant.daily",
            ["roll"]),
        static context => context.Entropy.GetDouble("roll") < 0.5 ? CoinSide.Heads : CoinSide.Tails,
        static (wager, outcome) => outcome == CoinSide.Heads ? checked(wager.Amount * 2) : 0);

    private sealed record ContextCommand(GameCommandContext Context) : IPlayerGameCommand
    {
        public long UserId => Context.UserId;
        public string DisplayName => Context.DisplayName;
        public long ChatId => Context.ChatId;
        public string CommandId => Context.CommandId;
    }

    private sealed class ContextDescriptor(GameDefinition game)
        : PlayerGameExecutionDescriptor<ContextCommand, NoGameState, string>(game);

    private sealed class InstantGame : IInstantWagerGame;

    private enum CoinSide
    {
        Heads,
        Tails,
    }

    private sealed class EmptyQuotaPolicy : IGameDailyQuotaPolicy
    {
        public IReadOnlyList<QuotaIdentity> Create(GameDailyQuotaRequest request) => [];
    }
}

using BotFramework.Contracts.Messaging;
using BotFramework.Host.Execution;
using BotFramework.Host.Execution.DeferredOutcome;
using BotFramework.Sdk.Events.Contracts;
using BotFramework.Sdk.Events.Meta;
using BotFramework.Sdk.Execution;
using BotFramework.Sdk.Execution.DeferredOutcome;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CasinoShiz.Tests;

public sealed class DeferredOutcomeWagerActionTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Place_ValidWager_DebitsAdvancesStateAndConsumesQuota()
    {
        var decision = Place().Decide(PlaceInput(amount: 25));

        Assert.Equal(DecisionStatus.Accepted, decision.Status);
        Assert.Equal(DeferredOutcomeWagerPlaceStatus.Accepted, decision.Result.Status);
        Assert.Equal(75, decision.Result.Balance);
        Assert.Equal(new DeferredOutcomeWagerQuotaUsage(1, 5), decision.Result.Quota);
        Assert.Equal(new DeferredOutcomeWager(10, 20, 25, Now), decision.NewState.PendingWager);
        Assert.Equal(1, decision.NewState.Revision);
        Assert.Equal(EconomyEffect.Debit(25, "coin-flip.bet"), Assert.Single(decision.Economy));
        Assert.Equal(QuotaEffect.Consume("coin-flip.daily"), Assert.Single(decision.Quotas));
        Assert.IsType<TestEvent>(Assert.Single(decision.Events));
    }

    [Theory]
    [InlineData(0, DeferredOutcomeWagerPlaceStatus.InvalidAmount, "invalid_amount")]
    [InlineData(101, DeferredOutcomeWagerPlaceStatus.InvalidAmount, "invalid_amount")]
    [InlineData(25, DeferredOutcomeWagerPlaceStatus.InsufficientBalance, "insufficient_balance")]
    public void Place_InvalidOrUnfundedWager_IsRejectedWithoutEffects(
        long amount,
        DeferredOutcomeWagerPlaceStatus expectedStatus,
        string expectedReason)
    {
        var balance = expectedStatus == DeferredOutcomeWagerPlaceStatus.InsufficientBalance ? 10 : 100;
        var decision = Place().Decide(PlaceInput(amount: amount, balance: balance));

        Assert.Equal(DecisionStatus.Rejected, decision.Status);
        Assert.Equal(expectedStatus, decision.Result.Status);
        Assert.Equal(expectedReason, decision.RejectionReason);
        Assert.Equal(DeferredOutcomeWagerState.Empty, decision.NewState);
        Assert.Empty(decision.EffectSet.MaterializeEffects());
        Assert.Empty(decision.Events);
    }

    [Fact]
    public void Place_PendingWagerAndDailyLimit_AreReportedBeforeDebit()
    {
        var pending = new DeferredOutcomeWager(10, 20, 15, Now.AddMinutes(-1));
        var alreadyPending = Place().Decide(PlaceInput(state: new(7, pending)));
        var quotaExceeded = Place().Decide(PlaceInput(quota: new(5, 5)));

        Assert.Equal(DeferredOutcomeWagerPlaceStatus.AlreadyPending, alreadyPending.Result.Status);
        Assert.Equal("already_pending", alreadyPending.RejectionReason);
        Assert.Equal(DeferredOutcomeWagerPlaceStatus.DailyQuotaExceeded, quotaExceeded.Result.Status);
        Assert.Equal("daily_quota_exceeded", quotaExceeded.RejectionReason);
        Assert.Empty(alreadyPending.Economy);
        Assert.Empty(quotaExceeded.Quotas);
    }

    [Fact]
    public void Resolve_ValidOutcome_CreditsPayoutClearsStateAndPassesEntropyToEvents()
    {
        var state = new DeferredOutcomeWagerState(1, new DeferredOutcomeWager(10, 20, 25, Now));
        var decision = Resolve().Decide(ResolveInput(state, CoinSide.Heads));

        Assert.Equal(DecisionStatus.Accepted, decision.Status);
        Assert.Equal(DeferredOutcomeWagerResolveStatus.Resolved, decision.Result.Status);
        Assert.Equal((25L, 50L, 125L), (decision.Result.Stake, decision.Result.Payout, decision.Result.Balance));
        Assert.Equal(new DeferredOutcomeWagerQuotaUsage(1, 5), decision.Result.Quota);
        Assert.Equal(new DeferredOutcomeWagerState(2, null), decision.NewState);
        Assert.Equal(EconomyEffect.Credit(50, "coin-flip.payout"), Assert.Single(decision.Economy));
        var completed = Assert.IsType<TestEvent>(Assert.Single(decision.Events));
        Assert.Equal("resolved:0.25", completed.Kind);
    }

    [Fact]
    public void Resolve_RequiresMatchingPendingWager_WithoutEffects()
    {
        var state = new DeferredOutcomeWagerState(1, new DeferredOutcomeWager(99, 20, 25, Now));
        var decision = Resolve().Decide(ResolveInput(state, CoinSide.Tails));

        Assert.Equal(DecisionStatus.Rejected, decision.Status);
        Assert.Equal(DeferredOutcomeWagerResolveStatus.PendingWagerMismatch, decision.Result.Status);
        Assert.Equal("pending_wager_mismatch", decision.RejectionReason);
        Assert.Empty(decision.EffectSet.MaterializeEffects());
        Assert.Empty(decision.Events);
    }

    [Fact]
    public void Resolve_DefaultDefinition_EmitsNormalizedCompletionEvent()
    {
        var definition = new DeferredOutcomeWagerDefinition<CoinFlipGame, CoinSide>(
            "coin-flip-default-event",
            "Coin flip",
            static (wager, outcome) => outcome == CoinSide.Heads ? wager.Amount * 2 : 0);
        var action = new DeferredOutcomeWagerResolveAction<CoinFlipGame, CoinSide>(definition);
        var state = new DeferredOutcomeWagerState(1, new DeferredOutcomeWager(10, 20, 25, Now));
        var input = new GameActionInput<
            DeferredOutcomeWagerState,
            DeferredOutcomeWagerResolveCommand<CoinFlipGame, CoinSide>>(
            new(10, "alice", 20, CoinSide.Heads, "resolve-default-event"),
            state,
            new WalletSnapshot(75),
            new Dictionary<string, QuotaSnapshot>(),
            EntropyValue.Empty,
            Now);

        var decision = action.Decide(input);

        var completion = Assert.IsType<GameCompletedMetaEvent>(Assert.Single(decision.Events));
        Assert.Equal(("coin-flip-default-event", 25L, 50L, true),
            (completion.GameKey, completion.Stake, completion.Payout, completion.IsWin));
    }

    [Fact]
    public void Abort_PendingWager_RefundsAndRestoresQuota()
    {
        var state = new DeferredOutcomeWagerState(1, new DeferredOutcomeWager(10, 20, 25, Now));
        var decision = Abort().Decide(AbortInput(state));

        Assert.Equal(DecisionStatus.Accepted, decision.Status);
        Assert.True(decision.Result.Aborted);
        Assert.Equal(new DeferredOutcomeWagerState(2, null), decision.NewState);
        Assert.Equal(EconomyEffect.Credit(25, "coin-flip.delivery_failed.refund"), Assert.Single(decision.Economy));
        Assert.Equal(QuotaEffect.Restore("coin-flip.daily"), Assert.Single(decision.Quotas));
        Assert.IsType<TestEvent>(Assert.Single(decision.Events));
    }

    [Fact]
    public void QuotaFreeDefinition_DoesNotRequireOrEmitQuota()
    {
        var definition = new DeferredOutcomeWagerDefinition<CoinFlipGame, CoinSide>(
            "coin-flip-free",
            "Coin flip",
            static (wager, outcome) => outcome == CoinSide.Heads ? wager.Amount * 2 : 0);
        var action = new DeferredOutcomeWagerPlaceAction<CoinFlipGame, CoinSide>(definition);
        var input = new GameActionInput<
            DeferredOutcomeWagerState,
            DeferredOutcomeWagerPlaceCommand<CoinFlipGame, CoinSide>>(
            new(10, "alice", 20, 25, 100, "place-free"),
            DeferredOutcomeWagerState.Empty,
            new WalletSnapshot(100),
            new Dictionary<string, QuotaSnapshot>(),
            EntropyValue.Empty,
            Now);

        var decision = action.Decide(input);

        Assert.True(decision.Result.Accepted);
        Assert.Null(decision.Result.Quota);
        Assert.Empty(decision.Quotas);
    }

    [Fact]
    public void Registration_AddsCompleteLifecycleWithFrameworkStateStores()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IGameDailyQuotaPolicy, EmptyGameDailyQuotaPolicy>();
        services.AddDeferredOutcomeWagerGame(Definition());

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var serviceProvider = scope.ServiceProvider;

        Assert.IsType<DeferredOutcomeWagerPlaceAction<CoinFlipGame, CoinSide>>(
            serviceProvider.GetRequiredService<IGameAction<
                DeferredOutcomeWagerPlaceCommand<CoinFlipGame, CoinSide>,
                DeferredOutcomeWagerState,
                DeferredOutcomeWagerPlaceResult>>());
        Assert.IsType<DeferredOutcomeWagerPlaceDescriptor<CoinFlipGame, CoinSide>>(
            serviceProvider.GetRequiredService<GameExecutionDescriptor<
                DeferredOutcomeWagerPlaceCommand<CoinFlipGame, CoinSide>,
                DeferredOutcomeWagerState,
                DeferredOutcomeWagerPlaceResult>>());
        Assert.IsType<PostgresJsonGameStateStore<
            DeferredOutcomeWagerPlaceCommand<CoinFlipGame, CoinSide>,
            DeferredOutcomeWagerState,
            DeferredOutcomeWagerPlaceResult>>(
            serviceProvider.GetRequiredService<IGameStateStore<
                DeferredOutcomeWagerPlaceCommand<CoinFlipGame, CoinSide>,
                DeferredOutcomeWagerState>>());
    }

    private static DeferredOutcomeWagerPlaceAction<CoinFlipGame, CoinSide> Place() => new(Definition());

    private static DeferredOutcomeWagerResolveAction<CoinFlipGame, CoinSide> Resolve() => new(Definition());

    private static DeferredOutcomeWagerAbortAction<CoinFlipGame, CoinSide> Abort() => new(Definition());

    private static DeferredOutcomeWagerDefinition<CoinFlipGame, CoinSide> Definition() => new(
        "coin-flip",
        "Coin flip",
        static (wager, outcome) => outcome == CoinSide.Heads ? checked(wager.Amount * 2) : 0,
        new DeferredOutcomeWagerDailyQuota("coin-flip.daily"),
        entropyNames: ["drop"],
        createPlacedEvents: static context => [new TestEvent("placed", context.OccurredAt)],
        createResolvedEvents: static context =>
        [new TestEvent($"resolved:{context.Entropy.GetDouble("drop")}", context.OccurredAt)],
        createAbortedEvents: static context => [new TestEvent("aborted", context.OccurredAt)],
        publishCompletionEvent: false);

    private static GameActionInput<
        DeferredOutcomeWagerState,
        DeferredOutcomeWagerPlaceCommand<CoinFlipGame, CoinSide>> PlaceInput(
        long amount = 25,
        long balance = 100,
        DeferredOutcomeWagerState? state = null,
        QuotaSnapshot? quota = null) =>
        new(
            new(10, "alice", 20, amount, 100, "place"),
            state ?? DeferredOutcomeWagerState.Empty,
            new WalletSnapshot(balance),
            Quotas(quota ?? new(0, 5)),
            Entropy(),
            Now)
        {
            Channel = BotChannel.Telegram,
        };

    private static GameActionInput<
        DeferredOutcomeWagerState,
        DeferredOutcomeWagerResolveCommand<CoinFlipGame, CoinSide>> ResolveInput(
        DeferredOutcomeWagerState state,
        CoinSide outcome) =>
        new(
            new(10, "alice", 20, outcome, "resolve"),
            state,
            new WalletSnapshot(75),
            Quotas(new(1, 5)),
            Entropy(),
            Now)
        {
            Channel = BotChannel.Telegram,
        };

    private static GameActionInput<
        DeferredOutcomeWagerState,
        DeferredOutcomeWagerAbortCommand<CoinFlipGame, CoinSide>> AbortInput(
        DeferredOutcomeWagerState state) =>
        new(
            new(10, "alice", 20, "abort"),
            state,
            new WalletSnapshot(75),
            Quotas(new(1, 5)),
            Entropy(),
            Now)
        {
            Channel = BotChannel.Telegram,
        };

    private static IReadOnlyDictionary<string, QuotaSnapshot> Quotas(QuotaSnapshot quota) =>
        new Dictionary<string, QuotaSnapshot>(StringComparer.Ordinal)
        {
            ["coin-flip.daily"] = quota,
        };

    private static EntropyValue Entropy() =>
        new([KeyValuePair.Create("drop", 0.25)]);

    private sealed class CoinFlipGame : IDeferredOutcomeWagerGame;

    private enum CoinSide
    {
        Heads,
        Tails,
    }

    private sealed record TestEvent(string Kind, DateTimeOffset At) : IDomainEvent
    {
        public string EventType => $"test.{Kind}";
        public long OccurredAt => At.ToUnixTimeMilliseconds();
    }

    private sealed class EmptyGameDailyQuotaPolicy : IGameDailyQuotaPolicy
    {
        public IReadOnlyList<QuotaIdentity> Create(GameDailyQuotaRequest request) => [];
    }
}

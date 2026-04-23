using BotFramework.Sdk;
using Games.DiceCube;
using Games.Darts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CasinoShiz.Tests;

[Collection("MiniGameSession")]
public class DartsServiceTests
{
    private const int CmdMsg = 1;
    private const int BotMsg = 9001;

    public DartsServiceTests()
    {
        BotMiniGameSession.DangerousResetAllForTests();
        DartsDiceRoundBinding.DangerousResetAllForTests();
    }

    private static DartsService MakeService(
        FakeEconomicsService? economics = null,
        InMemoryDartsRoundStore? rounds = null,
        IMiniGameSessionGhostHeal? ghostHeal = null,
        IDartsRollQueue? queue = null,
        NullEventBus? bus = null) =>
        new(
            economics ?? new FakeEconomicsService(),
            new NullAnalyticsService(),
            rounds ?? new InMemoryDartsRoundStore(),
            ghostHeal ?? new NullMiniGameSessionGhostHeal(),
            bus ?? new NullEventBus(),
            queue ?? new DartsRollQueue(),
            Options.Create(new DartsOptions()));

    private static async Task ArmAsync(
        InMemoryDartsRoundStore rounds, long roundId, long chatId, int botMessageId = BotMsg)
    {
        Assert.True(await rounds.TryMarkAwaitingOutcomeAsync(roundId, botMessageId, default));
        DartsDiceRoundBinding.Bind(chatId, botMessageId, roundId);
    }

    [Fact]
    public async Task PlaceBetAsync_StaleDiceCubeSession_NoPendingRow_AllowsBet()
    {
        BotMiniGameSession.RegisterPlacedBet(1, 100, MiniGameIds.DiceCube);
        var diceStore = new InMemoryDiceCubeBetStore();
        var heal = new LocalMiniGameSessionGhostHeal(diceCube: diceStore);
        var svc = MakeService(ghostHeal: heal);
        var result = await svc.PlaceBetAsync(1, "u", 100, 10, CmdMsg, default);
        Assert.Equal(DartsBetError.None, result.Error);
    }

    [Fact]
    public async Task PlaceBetAsync_ZeroAmount_ReturnsInvalidAmount()
    {
        var svc = MakeService();
        var result = await svc.PlaceBetAsync(1, "u", 100, 0, CmdMsg, default);
        Assert.Equal(DartsBetError.InvalidAmount, result.Error);
    }

    [Fact]
    public async Task PlaceBetAsync_NegativeAmount_ReturnsInvalidAmount()
    {
        var svc = MakeService();
        var result = await svc.PlaceBetAsync(1, "u", 100, -50, CmdMsg, default);
        Assert.Equal(DartsBetError.InvalidAmount, result.Error);
    }

    [Fact]
    public async Task PlaceBetAsync_InsufficientBalance_ReturnsNotEnoughCoins()
    {
        var econ = new FakeEconomicsService { StartingBalance = 10 };
        var svc = MakeService(economics: econ);
        var result = await svc.PlaceBetAsync(1, "u", 100, 100, CmdMsg, default);
        Assert.Equal(DartsBetError.NotEnoughCoins, result.Error);
    }

    [Fact]
    public async Task PlaceBetAsync_InsufficientBalance_ReturnsCurrentBalance()
    {
        var econ = new FakeEconomicsService { StartingBalance = 10 };
        var svc = MakeService(economics: econ);
        var result = await svc.PlaceBetAsync(1, "u", 100, 100, CmdMsg, default);
        Assert.Equal(10, result.Balance);
    }

    [Fact]
    public async Task PlaceBetAsync_Success_ReturnsNoneError()
    {
        var svc = MakeService();
        var result = await svc.PlaceBetAsync(1, "u", 100, 50, CmdMsg, default);
        Assert.Equal(DartsBetError.None, result.Error);
    }

    [Fact]
    public async Task PlaceBetAsync_Success_DebitsAmount()
    {
        var econ = new FakeEconomicsService();
        var svc = MakeService(economics: econ);
        await svc.PlaceBetAsync(1, "u", 100, 50, CmdMsg, default);
        Assert.Single(econ.Debits);
        Assert.Equal(50, econ.Debits[0].Amount);
    }

    [Fact]
    public async Task PlaceBetAsync_Success_BalanceReducedByBet()
    {
        var econ = new FakeEconomicsService { StartingBalance = 200 };
        var svc = MakeService(economics: econ);
        var result = await svc.PlaceBetAsync(1, "u", 100, 50, CmdMsg, default);
        Assert.Equal(150, result.Balance);
    }

    [Fact]
    public async Task PlaceBetAsync_Success_AmountMatchesBet()
    {
        var svc = MakeService();
        var result = await svc.PlaceBetAsync(1, "u", 100, 75, CmdMsg, default);
        Assert.Equal(75, result.Amount);
    }

    [Fact]
    public async Task PlaceBetAsync_SecondRoundSameChat_SucceedsWithQueuedAhead()
    {
        var rounds = new InMemoryDartsRoundStore();
        var svc = MakeService(rounds: rounds);
        var first = await svc.PlaceBetAsync(1, "u", 100, 50, CmdMsg, default);
        var second = await svc.PlaceBetAsync(1, "u", 100, 30, CmdMsg, default);
        Assert.Equal(DartsBetError.None, first.Error);
        Assert.Equal(DartsBetError.None, second.Error);
        Assert.Equal(0, first.QueuedAhead);
        Assert.Equal(1, second.QueuedAhead);
        Assert.NotEqual(first.RoundId, second.RoundId);
    }

    [Fact]
    public async Task PlaceBetAsync_SameUserDifferentChats_BothSucceed()
    {
        var econ = new FakeEconomicsService { StartingBalance = 1_000 };
        var rounds = new InMemoryDartsRoundStore();
        var svc = MakeService(economics: econ, rounds: rounds);
        var r1 = await svc.PlaceBetAsync(1, "u", chatId: 100, 50, CmdMsg, default);
        var r2 = await svc.PlaceBetAsync(1, "u", chatId: 200, 30, CmdMsg, default);
        Assert.Equal(DartsBetError.None, r1.Error);
        Assert.Equal(DartsBetError.None, r2.Error);
    }

    [Fact]
    public async Task ThrowAsync_NoBet_ReturnsNoBet()
    {
        var svc = MakeService();
        var result = await svc.ThrowAsync(9999, 1, "u", 100, 1, 4, default);
        Assert.Equal(DartsThrowOutcome.NoBet, result.Outcome);
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 0)]
    [InlineData(3, 0)]
    [InlineData(4, 1)]
    [InlineData(5, 2)]
    [InlineData(6, 2)]
    public async Task ThrowAsync_ReturnsCorrectMultiplier(int face, int expectedMultiplier)
    {
        var rounds = new InMemoryDartsRoundStore();
        var svc = MakeService(rounds: rounds);
        var pr = await svc.PlaceBetAsync(1, "u", 100, 50, CmdMsg, default);
        await ArmAsync(rounds, pr.RoundId, 100);
        var result = await svc.ThrowAsync(pr.RoundId, 1, "u", 100, BotMsg, face, default);
        Assert.Equal(expectedMultiplier, result.Multiplier);
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 0)]
    [InlineData(3, 0)]
    [InlineData(4, 50)]
    [InlineData(5, 100)]
    [InlineData(6, 100)]
    public async Task ThrowAsync_ReturnsCorrectPayout(int face, int expectedPayout)
    {
        var rounds = new InMemoryDartsRoundStore();
        var svc = MakeService(rounds: rounds);
        var pr = await svc.PlaceBetAsync(1, "u", 100, 50, CmdMsg, default);
        await ArmAsync(rounds, pr.RoundId, 100);
        var result = await svc.ThrowAsync(pr.RoundId, 1, "u", 100, BotMsg, face, default);
        Assert.Equal(expectedPayout, result.Payout);
    }

    [Fact]
    public void DartsBullseye_DiceCube_Face6_SameDefaultMultiplier()
    {
        var d = DartsService.Multipliers[6];
        var c = DiceCubeService.BuildMultipliers(new DiceCubeOptions())[6];
        Assert.Equal(c, d);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public async Task ThrowAsync_WinningFace_CreditsPayout(int face)
    {
        var econ = new FakeEconomicsService();
        var rounds = new InMemoryDartsRoundStore();
        var svc = MakeService(economics: econ, rounds: rounds);
        var pr = await svc.PlaceBetAsync(1, "u", 100, 50, CmdMsg, default);
        await ArmAsync(rounds, pr.RoundId, 100);
        await svc.ThrowAsync(pr.RoundId, 1, "u", 100, BotMsg, face, default);
        Assert.Single(econ.Credits);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task ThrowAsync_LosingFace_NoCredit(int face)
    {
        var econ = new FakeEconomicsService();
        var rounds = new InMemoryDartsRoundStore();
        var svc = MakeService(economics: econ, rounds: rounds);
        var pr = await svc.PlaceBetAsync(1, "u", 100, 50, CmdMsg, default);
        await ArmAsync(rounds, pr.RoundId, 100);
        await svc.ThrowAsync(pr.RoundId, 1, "u", 100, BotMsg, face, default);
        Assert.Empty(econ.Credits);
    }

    [Fact]
    public async Task ThrowAsync_AfterThrow_BetIsDeleted()
    {
        var rounds = new InMemoryDartsRoundStore();
        var svc = MakeService(rounds: rounds);
        var pr = await svc.PlaceBetAsync(1, "u", 100, 50, CmdMsg, default);
        await ArmAsync(rounds, pr.RoundId, 100);
        await svc.ThrowAsync(pr.RoundId, 1, "u", 100, BotMsg, 4, default);
        var second = await svc.ThrowAsync(pr.RoundId, 1, "u", 100, BotMsg, 4, default);
        Assert.Equal(DartsThrowOutcome.NoBet, second.Outcome);
    }

    [Fact]
    public async Task ThrowAsync_UnknownFace_NoPayout()
    {
        var rounds = new InMemoryDartsRoundStore();
        var svc = MakeService(rounds: rounds);
        var pr = await svc.PlaceBetAsync(1, "u", 100, 50, CmdMsg, default);
        await ArmAsync(rounds, pr.RoundId, 100);
        var result = await svc.ThrowAsync(pr.RoundId, 1, "u", 100, BotMsg, 99, default);
        Assert.Equal(0, result.Payout);
        Assert.Equal(0, result.Multiplier);
    }

    [Fact]
    public async Task ThrowAsync_Success_ReturnsThrowOutcome()
    {
        var rounds = new InMemoryDartsRoundStore();
        var svc = MakeService(rounds: rounds);
        var pr = await svc.PlaceBetAsync(1, "u", 100, 50, CmdMsg, default);
        await ArmAsync(rounds, pr.RoundId, 100);
        var result = await svc.ThrowAsync(pr.RoundId, 1, "u", 100, BotMsg, 4, default);
        Assert.Equal(DartsThrowOutcome.Thrown, result.Outcome);
    }

    [Fact]
    public async Task ThrowAsync_Success_ReturnsFace()
    {
        var rounds = new InMemoryDartsRoundStore();
        var svc = MakeService(rounds: rounds);
        var pr = await svc.PlaceBetAsync(1, "u", 100, 50, CmdMsg, default);
        await ArmAsync(rounds, pr.RoundId, 100);
        var result = await svc.ThrowAsync(pr.RoundId, 1, "u", 100, BotMsg, 6, default);
        Assert.Equal(6, result.Face);
    }

    [Fact]
    public async Task ThrowAsync_Success_ReturnsBetAmount()
    {
        var rounds = new InMemoryDartsRoundStore();
        var svc = MakeService(rounds: rounds);
        var pr = await svc.PlaceBetAsync(1, "u", 100, 50, CmdMsg, default);
        await ArmAsync(rounds, pr.RoundId, 100);
        var result = await svc.ThrowAsync(pr.RoundId, 1, "u", 100, BotMsg, 4, default);
        Assert.Equal(50, result.Bet);
    }

    [Fact]
    public async Task ThrowAsync_PublishesThrowCompletedEvent()
    {
        var bus = new NullEventBus();
        var rounds = new InMemoryDartsRoundStore();
        var svc = MakeService(rounds: rounds, bus: bus);
        var pr = await svc.PlaceBetAsync(1, "u", 100, 50, CmdMsg, default);
        await ArmAsync(rounds, pr.RoundId, 100);
        await svc.ThrowAsync(pr.RoundId, 1, "u", 100, BotMsg, 4, default);
        Assert.Single(bus.Published);
        Assert.IsType<DartsThrowCompleted>(bus.Published[0]);
    }

    [Fact]
    public void Multipliers_ContainsAllSixFaces()
    {
        for (var face = 1; face <= 6; face++)
            Assert.True(DartsService.Multipliers.ContainsKey(face));
    }

    [Fact]
    public async Task DispatcherJob_RequeuesPersistedQueuedRounds_OnStartup()
    {
        var rounds = new InMemoryDartsRoundStore();
        var roundId = await rounds.InsertQueuedAsync(
            new DartsRound(0, 42, 100, 25, DateTimeOffset.UtcNow, DartsRoundStatus.Queued, null, CmdMsg),
            default);
        var queue = new RecordingDartsRollQueue();
        var services = new ServiceCollection()
            .AddSingleton<IDartsRoundStore>(rounds)
            .BuildServiceProvider();
        var job = new DartsRollDispatcherJob(
            queue,
            services,
            null!,
            NullLogger<DartsRollDispatcherJob>.Instance);

        using var cts = new CancellationTokenSource();
        var run = job.RunAsync(cts.Token);
        await Task.Delay(20);
        await cts.CancelAsync();
        await run;

        var queued = Assert.Single(queue.Enqueued);
        Assert.Equal(roundId, queued.RoundId);
        Assert.Equal(100, queued.ChatId);
        Assert.Equal(42, queued.UserId);
        Assert.Equal(CmdMsg, queued.ReplyToMessageId);
    }
}

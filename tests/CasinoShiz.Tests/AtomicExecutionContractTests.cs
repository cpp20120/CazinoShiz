using BotFramework.Host.Execution;
using BotFramework.Sdk.Execution;
using Games.Dice.Application.Execution;
using Games.DiceCube.Application.Execution;
using Games.Blackjack.Application.Execution;
using Games.Basketball.Application.Execution;
using Games.Bowling.Application.Execution;
using Games.Football.Application.Execution;
using Games.Darts.Application.Execution;
using Games.Darts.Application.Services;
using Games.Horse.Application.Execution;
using Games.Horse.Application.Jobs;
using Games.Horse.Application.Services;
using Games.Poker.Application.Execution;
using Games.Poker.Application.Jobs;
using Games.Poker.Application.Services;
using Games.Transfer.Application.Execution;
using Games.Transfer.Application.Services;
using Games.Challenges.Application.Execution;
using Games.Challenges.Application.Services;
using Games.Redeem.Application.Execution;
using Games.Redeem.Application.Services;
using Games.SecretHitler.Application.Execution;
using Games.SecretHitler.Application.Services;
using Games.PixelBattle.Application;
using Games.PixelBattle.Application.Execution;
using Games.PixelBattle.Contracts;
using Games.Pick.Application.Execution;
using Games.Pick.Application.Jobs;
using Games.Pick.Application.Results;
using Games.Pick.Application.Services;
using Games.Pick.Infrastructure.Results;
using Games.Pick.Domain.Results;
using BotFramework.Scheduling.Abstractions;
using Xunit;

namespace CasinoShiz.Tests;

public sealed class AtomicExecutionContractTests
{
    [Fact]
    public void FrameworkMigrations_ContainAtomicCommandInboxUpgrade()
    {
        var migration = Assert.Single(
            new FrameworkMigrations().Migrations,
            item => string.Equals(item.Id, "022_atomic_game_command_inbox", StringComparison.Ordinal));

        Assert.Contains("game_command_idempotency", migration.Sql, StringComparison.Ordinal);
        Assert.Contains("result_type", migration.Sql, StringComparison.Ordinal);
        Assert.Contains("aggregate_id", migration.Sql, StringComparison.Ordinal);

        var outbox = Assert.Single(
            new FrameworkMigrations().Migrations,
            item => string.Equals(item.Id, "023_atomic_game_event_outbox", StringComparison.Ordinal));
        Assert.Contains("UNIQUE (command_id, event_index)", outbox.Sql, StringComparison.Ordinal);

        var aggregateStates = Assert.Single(
            new FrameworkMigrations().Migrations,
            item => string.Equals(item.Id, "024_versioned_game_aggregate_states", StringComparison.Ordinal));
        Assert.Contains("game_aggregate_states", aggregateStates.Sql, StringComparison.Ordinal);
        Assert.Contains("PRIMARY KEY (game_id, aggregate_id)", aggregateStates.Sql, StringComparison.Ordinal);

        var scheduleOutbox = Assert.Single(
            new FrameworkMigrations().Migrations,
            item => string.Equals(item.Id, "025_atomic_game_schedule_outbox", StringComparison.Ordinal));
        Assert.Contains("game_schedule_outbox", scheduleOutbox.Sql, StringComparison.Ordinal);
        Assert.Contains("UNIQUE (command_id, effect_index)", scheduleOutbox.Sql, StringComparison.Ordinal);

        var scheduleOwnership = Assert.Single(
            new FrameworkMigrations().Migrations,
            item => string.Equals(item.Id, "027_schedule_outbox_ownership", StringComparison.Ordinal));
        Assert.Contains("game_id", scheduleOwnership.Sql, StringComparison.Ordinal);
        Assert.Contains("split_part(schedule_id", scheduleOwnership.Sql, StringComparison.Ordinal);
    }

    [Fact]
    public void ExecutionCapabilities_AreTransactionAware()
    {
        AssertSessionParameter(typeof(ICommandInbox));
        AssertSessionParameter(typeof(IAtomicEconomics));
        AssertSessionParameter(typeof(IAtomicQuotaStore));
        AssertSessionParameter(typeof(ITransactionalScheduleCollector));
    }

    [Fact]
    public void LockKeys_AreStableAndScoped()
    {
        var wallet = new WalletIdentity(42, -100);
        var quota = new QuotaIdentity(
            "dice.daily-roll",
            "dice",
            42,
            -100,
            new DateOnly(2026, 7, 11),
            10);

        Assert.Equal("wallet:-100:42", wallet.LockKey);
        Assert.Equal("quota:dice:-100:42:2026-07-11", quota.LockKey);
    }

    [Fact]
    public void DiceService_OrchestratesOnlyThroughAtomicExecutor()
    {
        var dependencies = typeof(DiceService)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        Assert.Contains(typeof(IAtomicGameExecutor<DiceCommand, NoGameState, DicePlayResult>), dependencies);
        Assert.DoesNotContain(typeof(IEconomicsService), dependencies);
        Assert.DoesNotContain(typeof(IDomainEventBus), dependencies);
    }

    [Fact]
    public void BasketballService_OrchestratesOnlyThroughAtomicExecutors()
    {
        var dependencies = typeof(BasketballService)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        Assert.Contains(
            typeof(IAtomicGameExecutor<BasketballPlaceBetCommand, BasketballBetState, BasketballBetResult>),
            dependencies);
        Assert.Contains(
            typeof(IAtomicGameExecutor<BasketballThrowCommand, BasketballBetState, BasketballThrowResult>),
            dependencies);
        Assert.DoesNotContain(typeof(IEconomicsService), dependencies);
        Assert.DoesNotContain(typeof(IDomainEventBus), dependencies);
        Assert.DoesNotContain(typeof(IAnalyticsService), dependencies);
    }

    [Fact]
    public void BowlingService_OrchestratesOnlyThroughAtomicExecutors()
    {
        var dependencies = typeof(BowlingService).GetConstructors().Single().GetParameters()
            .Select(parameter => parameter.ParameterType).ToArray();

        Assert.Contains(typeof(IAtomicGameExecutor<BowlingPlaceBetCommand, BowlingBetState, BowlingBetResult>), dependencies);
        Assert.Contains(typeof(IAtomicGameExecutor<BowlingRollCommand, BowlingBetState, BowlingRollResult>), dependencies);
        Assert.Contains(typeof(IAtomicGameExecutor<BowlingAbortCommand, BowlingBetState, BowlingAbortResult>), dependencies);
        Assert.DoesNotContain(typeof(IEconomicsService), dependencies);
        Assert.DoesNotContain(typeof(IDomainEventBus), dependencies);
        Assert.DoesNotContain(typeof(IAnalyticsService), dependencies);
        Assert.DoesNotContain(typeof(ITelegramDiceDailyRollLimiter), dependencies);
    }

    [Fact]
    public void FootballService_OrchestratesOnlyThroughAtomicExecutors()
    {
        var dependencies = typeof(FootballService).GetConstructors().Single().GetParameters()
            .Select(parameter => parameter.ParameterType).ToArray();

        Assert.Contains(typeof(IAtomicGameExecutor<FootballPlaceBetCommand, FootballBetState, FootballBetResult>), dependencies);
        Assert.Contains(typeof(IAtomicGameExecutor<FootballThrowCommand, FootballBetState, FootballThrowResult>), dependencies);
        Assert.Contains(typeof(IAtomicGameExecutor<FootballAbortCommand, FootballBetState, FootballAbortResult>), dependencies);
        Assert.DoesNotContain(typeof(IEconomicsService), dependencies);
        Assert.DoesNotContain(typeof(IDomainEventBus), dependencies);
        Assert.DoesNotContain(typeof(IAnalyticsService), dependencies);
        Assert.DoesNotContain(typeof(ITelegramDiceDailyRollLimiter), dependencies);
    }

    [Fact]
    public void PickService_OrchestratesPrimaryGameThroughAtomicExecutor()
    {
        var dependencies = typeof(PickService).GetConstructors().Single().GetParameters()
            .Select(parameter => parameter.ParameterType).ToArray();

        Assert.Contains(typeof(IAtomicGameExecutor<PickCommand, PickGameState, PickResult>), dependencies);
        Assert.DoesNotContain(typeof(IEconomicsService), dependencies);
        Assert.DoesNotContain(typeof(IAnalyticsService), dependencies);
        Assert.DoesNotContain(typeof(IDomainEventBus), dependencies);
    }

    [Fact]
    public void PickLotteries_OrchestrateMutationsOnlyThroughAtomicExecutors()
    {
        var quick = typeof(PickLotteryService).GetConstructors().Single().GetParameters()
            .Select(parameter => parameter.ParameterType).ToArray();
        Assert.Contains(typeof(IAtomicGameExecutor<QuickLotteryOpenCommand, QuickLotteryState, LotteryOpenResult>), quick);
        Assert.Contains(typeof(IAtomicGameExecutor<QuickLotteryJoinCommand, QuickLotteryState, LotteryJoinResult>), quick);
        Assert.Contains(typeof(IAtomicGameExecutor<QuickLotterySettleCommand, QuickLotteryState, LotterySettleResult>), quick);
        Assert.DoesNotContain(typeof(IEconomicsService), quick);
        Assert.DoesNotContain(typeof(IAnalyticsService), quick);

        var daily = typeof(PickDailyLotteryService).GetConstructors().Single().GetParameters()
            .Select(parameter => parameter.ParameterType).ToArray();
        Assert.Contains(typeof(IAtomicGameExecutor<DailyBuyCommand, DailyLotteryState, DailyBuyResult>), daily);
        Assert.Contains(typeof(IAtomicGameExecutor<DailySettleCommand, DailyLotteryState, DailySettleResult>), daily);
        Assert.DoesNotContain(typeof(IEconomicsService), daily);
        Assert.DoesNotContain(typeof(IAnalyticsService), daily);
    }

    [Fact]
    public void PickLotterySweepers_AreQuartzRecurringCommands()
    {
        Assert.True(typeof(IRecurringScheduledCommand).IsAssignableFrom(typeof(PickLotterySweeperJob)));
        Assert.True(typeof(IRecurringScheduledCommand).IsAssignableFrom(typeof(PickDailyLotterySweeperJob)));
    }

    [Fact]
    public void DartsService_OrchestratesAllMutationPathsThroughAtomicExecutors()
    {
        var dependencies = typeof(DartsService).GetConstructors().Single().GetParameters()
            .Select(parameter => parameter.ParameterType).ToArray();
        Assert.Contains(typeof(IAtomicGameExecutor<DartsPlaceBetCommand, DartsQueuedState, DartsBetResult>), dependencies);
        Assert.Contains(typeof(IAtomicGameExecutor<DartsResolveRoundCommand, DartsQueuedState, DartsThrowResult>), dependencies);
        Assert.Contains(typeof(IAtomicGameExecutor<DartsAbortRoundCommand, DartsQueuedState, DartsAbortRoundResult>), dependencies);
        Assert.DoesNotContain(typeof(IEconomicsService), dependencies);
        Assert.DoesNotContain(typeof(IAnalyticsService), dependencies);
        Assert.DoesNotContain(typeof(IDomainEventBus), dependencies);
        Assert.DoesNotContain(typeof(ITelegramDiceDailyRollLimiter), dependencies);
    }

    [Fact]
    public void HorseService_UsesAtomicMultiWalletExecutionAndQuartz()
    {
        var dependencies = typeof(HorseService).GetConstructors().Single().GetParameters()
            .Select(parameter => parameter.ParameterType).ToArray();
        Assert.Contains(typeof(IAtomicGameExecutor<HorsePlaceBetCommand, HorseBetState, BetResult>), dependencies);
        Assert.Contains(typeof(IAtomicGameExecutor<HorseRunCommand, HorseRaceState, RaceOutcome>), dependencies);
        Assert.DoesNotContain(typeof(IEconomicsService), dependencies);
        Assert.DoesNotContain(typeof(IAnalyticsService), dependencies);
        Assert.DoesNotContain(typeof(IDomainEventBus), dependencies);
        Assert.True(typeof(IRecurringScheduledCommand).IsAssignableFrom(typeof(HorseRaceScheduledCommand)));
    }

    [Fact]
    public void TransferService_UsesAtomicTwoWalletExecution()
    {
        var dependencies = typeof(TransferService).GetConstructors().Single().GetParameters()
            .Select(parameter => parameter.ParameterType).ToArray();
        Assert.Contains(typeof(IAtomicGameExecutor<TransferCommand, TransferState, TransferAttemptResult>), dependencies);
        Assert.DoesNotContain(typeof(IEconomicsService), dependencies);
        Assert.DoesNotContain(typeof(IAnalyticsService), dependencies);
    }

    [Fact]
    public void PokerService_SplitsStateOnlyCommandsFromAtomicEconomicsAndQuartzTimeouts()
    {
        var dependencies = typeof(PokerService).GetConstructors().Single().GetParameters()
            .Select(parameter => parameter.ParameterType).ToArray();
        Assert.Contains(typeof(IAtomicGameExecutor<PokerCreateCommand, PokerExecutionState, CreateResult>), dependencies);
        Assert.Contains(typeof(IAtomicGameExecutor<PokerPlayerTurnCommand, PokerExecutionState, ActionResult>), dependencies);
        Assert.Contains(typeof(IAtomicGameExecutor<PokerLeaveCommand, PokerExecutionState, LeaveResult>), dependencies);
        Assert.Contains(typeof(IGameStateExecutor<PokerStartCommand, PokerExecutionState, StartResult>), dependencies);
        Assert.Contains(typeof(IGameStateExecutor<PokerSetMessageCommand, PokerExecutionState, bool>), dependencies);
        Assert.DoesNotContain(typeof(IAtomicGameExecutor<PokerStartCommand, PokerExecutionState, StartResult>), dependencies);
        Assert.DoesNotContain(typeof(IAtomicGameExecutor<PokerSetMessageCommand, PokerExecutionState, bool>), dependencies);
        Assert.DoesNotContain(typeof(IEconomicsService), dependencies);
        Assert.DoesNotContain(typeof(IAnalyticsService), dependencies);
        Assert.DoesNotContain(typeof(IDomainEventBus), dependencies);
        Assert.True(typeof(IRecurringScheduledCommand).IsAssignableFrom(typeof(PokerTurnTimeoutJob)));
    }

    [Fact]
    public void ChallengeService_SplitsStateOnlyDeclineFromAtomicLifecycleExecutors()
    {
        var dependencies = typeof(ChallengeService).GetConstructors().Single().GetParameters()
            .Select(parameter => parameter.ParameterType).ToArray();
        Assert.Contains(typeof(IAtomicGameExecutor<ChallengeAcceptCommand, ChallengeExecutionState, ChallengeAcceptResult>), dependencies);
        Assert.Contains(typeof(IAtomicGameExecutor<ChallengeCompleteCommand, ChallengeExecutionState, ChallengeAcceptResult>), dependencies);
        Assert.Contains(typeof(IGameStateExecutor<ChallengeDeclineCommand, ChallengeExecutionState, ChallengeAcceptError>), dependencies);
        Assert.DoesNotContain(typeof(IAtomicGameExecutor<ChallengeDeclineCommand, ChallengeExecutionState, ChallengeAcceptError>), dependencies);
        Assert.DoesNotContain(typeof(IEconomicsService), dependencies);
        Assert.DoesNotContain(typeof(IAnalyticsService), dependencies);
    }

    [Fact]
    public void RedeemService_UsesStateOnlyIssueAndAtomicClaimExecutors()
    {
        var dependencies = typeof(RedeemService).GetConstructors().Single().GetParameters()
            .Select(parameter => parameter.ParameterType).ToArray();
        Assert.Contains(typeof(IGameStateExecutor<RedeemIssueCommand, RedeemExecutionState, Guid>), dependencies);
        Assert.DoesNotContain(typeof(IAtomicGameExecutor<RedeemIssueCommand, RedeemExecutionState, Guid>), dependencies);
        Assert.Contains(typeof(IAtomicGameExecutor<RedeemCompleteCommand, RedeemExecutionState, CompleteRedeemResult>), dependencies);
        Assert.DoesNotContain(typeof(IEconomicsService), dependencies);
        Assert.DoesNotContain(typeof(IAnalyticsService), dependencies);
        Assert.DoesNotContain(typeof(IDomainEventBus), dependencies);
        Assert.DoesNotContain(typeof(ITelegramDiceDailyRollLimiter), dependencies);
    }

    [Fact]
    public void SecretHitlerService_SplitsStateOnlyTurnCommandsFromAtomicEconomics()
    {
        var dependencies = typeof(SecretHitlerService).GetConstructors().Single().GetParameters()
            .Select(parameter => parameter.ParameterType).ToArray();
        Assert.Contains(typeof(IAtomicGameExecutor<ShCreateCommand, SecretHitlerExecutionState, ShCreateResult>), dependencies);
        Assert.Contains(typeof(IAtomicGameExecutor<ShVoteCommand, SecretHitlerExecutionState, ShVoteResult>), dependencies);
        Assert.Contains(typeof(IAtomicGameExecutor<ShEnactCommand, SecretHitlerExecutionState, ShEnactResult>), dependencies);
        Assert.Contains(typeof(IAtomicGameExecutor<ShLeaveCommand, SecretHitlerExecutionState, ShLeaveResult>), dependencies);
        Assert.Contains(typeof(IGameStateExecutor<ShStartCommand, SecretHitlerExecutionState, ShStartResult>), dependencies);
        Assert.Contains(typeof(IGameStateExecutor<ShNominateCommand, SecretHitlerExecutionState, ShNominateResult>), dependencies);
        Assert.Contains(typeof(IGameStateExecutor<ShDiscardCommand, SecretHitlerExecutionState, ShDiscardResult>), dependencies);
        Assert.Contains(typeof(IGameStateExecutor<ShPlayerMessageCommand, SecretHitlerExecutionState, bool>), dependencies);
        Assert.Contains(typeof(IGameStateExecutor<ShPublicMessageCommand, SecretHitlerExecutionState, bool>), dependencies);
        Assert.DoesNotContain(typeof(IAtomicGameExecutor<ShStartCommand, SecretHitlerExecutionState, ShStartResult>), dependencies);
        Assert.DoesNotContain(typeof(IAtomicGameExecutor<ShNominateCommand, SecretHitlerExecutionState, ShNominateResult>), dependencies);
        Assert.DoesNotContain(typeof(IAtomicGameExecutor<ShDiscardCommand, SecretHitlerExecutionState, ShDiscardResult>), dependencies);
        Assert.DoesNotContain(typeof(IAtomicGameExecutor<ShPlayerMessageCommand, SecretHitlerExecutionState, bool>), dependencies);
        Assert.DoesNotContain(typeof(IAtomicGameExecutor<ShPublicMessageCommand, SecretHitlerExecutionState, bool>), dependencies);
        Assert.DoesNotContain(typeof(IEconomicsService), dependencies);
        Assert.DoesNotContain(typeof(IAnalyticsService), dependencies);
        Assert.DoesNotContain(typeof(IDomainEventBus), dependencies);
        Assert.DoesNotContain(typeof(IDistributedGameLock), dependencies);
    }

    [Fact]
    public void PixelBattleService_UsesTileScopedStateExecutor()
    {
        var dependencies = typeof(PixelBattleService).GetConstructors().Single().GetParameters()
            .Select(parameter => parameter.ParameterType).ToArray();
        Assert.Contains(typeof(IGameStateExecutor<PixelBattleCommand, PixelBattleExecutionState,
            PixelUpdateResult>), dependencies);
        Assert.DoesNotContain(typeof(IAtomicGameExecutor<PixelBattleCommand, PixelBattleExecutionState,
            PixelUpdateResult>), dependencies);
        Assert.DoesNotContain(typeof(IWalletReadService), dependencies);

        var descriptor = new PixelBattleDescriptor();
        Assert.Equal("tile:10", descriptor.AggregateId(new(1, 10, "#FFFFFF", "a")));
        Assert.Equal("tile:11", descriptor.AggregateId(new(1, 11, "#FFFFFF", "b")));
    }

    [Fact]
    public void DiceCubeService_UsesAtomicExecutorForPlaceBetPilot()
    {
        var dependencies = typeof(DiceCubeService)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        Assert.Contains(
            typeof(IAtomicGameExecutor<DiceCubePlaceBetCommand, DiceCubePlaceBetState, CubeBetResult>),
            dependencies);
        Assert.Contains(
            typeof(IAtomicGameExecutor<DiceCubeRollCommand, DiceCubePlaceBetState, CubeRollResult>),
            dependencies);
        Assert.Contains(
            typeof(IAtomicGameExecutor<DiceCubeAbortCommand, DiceCubePlaceBetState, DiceCubeAbortResult>),
            dependencies);
        Assert.DoesNotContain(typeof(IEconomicsService), dependencies);
        Assert.DoesNotContain(typeof(IDomainEventBus), dependencies);
        Assert.DoesNotContain(typeof(IAnalyticsService), dependencies);
        Assert.DoesNotContain(typeof(ITelegramDiceDailyRollLimiter), dependencies);
    }

    [Fact]
    public void BlackjackService_OrchestratesOnlyThroughAtomicExecutors()
    {
        var dependencies = typeof(BlackjackService)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        Assert.Contains(
            typeof(IAtomicGameExecutor<BlackjackStartCommand, BlackjackGameState, BlackjackResult>),
            dependencies);
        Assert.Contains(
            typeof(IAtomicGameExecutor<BlackjackTurnCommand, BlackjackGameState, BlackjackResult>),
            dependencies);
        Assert.DoesNotContain(typeof(IEconomicsService), dependencies);
        Assert.DoesNotContain(typeof(IDomainEventBus), dependencies);
        Assert.DoesNotContain(typeof(IAnalyticsService), dependencies);
    }

    private static void AssertSessionParameter(Type capability)
    {
        Assert.All(
            capability.GetMethods(),
            method => Assert.Contains(
                method.GetParameters(),
                parameter => parameter.ParameterType == typeof(IGameExecutionSession)));
    }
}

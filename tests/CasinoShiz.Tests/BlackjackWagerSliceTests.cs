using System.Text.Json;
using BotFramework.Contracts.Ledger;
using BotFramework.Contracts.Messaging;
using BotFramework.Contracts.Wagering;
using BotFramework.Host.Wagering;
using BotFramework.Sdk.Execution;
using Games.Blackjack.Application.Execution;
using Games.Blackjack.Application.Wagering;
using Games.Blackjack.Contracts.Domain.Results;
using Games.Blackjack.Contracts.Integration;
using Games.Blackjack.Domain.Events;
using Games.Blackjack.Domain.Results;
using Xunit;

namespace CasinoShiz.Tests;

public sealed class BlackjackWagerSliceTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 12, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void WagerStateAndOutcomeDoNotCarryEconomyFields()
    {
        var state = ActiveState();
        var properties = typeof(BlackjackWagerState).GetProperties()
            .Select(property => property.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.DoesNotContain("Stake", properties);
        Assert.DoesNotContain("Balance", properties);
        Assert.DoesNotContain("Payout", properties);

        var decision = new BlackjackWagerStandAction().Decide(
            Input(
                new BlackjackWagerStand(
                    "op-1", "bet-1", "player-1", 42, "player", 1, "stand-1", Now),
                state));

        Assert.Equal(DecisionStatus.Accepted, decision.Status);
        Assert.Empty(decision.Economy);
        Assert.IsType<BlackjackWagerOutcomeDeclared>(Assert.Single(decision.Events));
        Assert.DoesNotContain("Payout", JsonSerializer.Serialize(decision.NewState));
    }

    [Fact]
    public void TimeoutIsOutcomeOnlyAndDoesNotMutateEconomy()
    {
        var state = ActiveState() with { TurnDeadline = Now.AddMinutes(-1) };
        var decision = new BlackjackWagerTimeoutAction().Decide(
            Input(
                new BlackjackWagerTimeout(
                    "op-1", "bet-1", "player-1", 42, "player", "timeout-1", Now),
                state));

        Assert.Equal(DecisionStatus.Accepted, decision.Status);
        Assert.Equal(TurnGameStatus.Completed, decision.NewState.Status);
        Assert.Empty(decision.Economy);
        Assert.Single(decision.Events);
    }

    [Fact]
    public async Task ReservePlaySettleUsesWageringTermsOnlyAtSettlement()
    {
        var terms = new WagerTermsSnapshot("blackjack.v1", 10, "coins", "standard");
        var operation = new WagerOperation(
            "op-1",
            "bet-1",
            "blackjack",
            "player-1",
            JsonSerializer.Serialize(new BlackjackWagerStartInput(42, "player", 60_000)),
            JsonSerializer.Serialize(terms),
            WagerOperationStatus.Pending,
            null,
            null,
            Now,
            Now);
        var store = new MemoryWagerStore();
        var commands = new RecordingCommandPublisher();
        var events = new RecordingEventPublisher();
        var coordinator = new WageringCoordinator(
            store,
            commands,
            [new BlackjackWagerGameCommandFactory()],
            [new BlackjackWagerSettlementFactory()],
            events);

        await coordinator.HandleAsync(
            new WagerRequested(
                operation.OperationId,
                operation.BetId,
                operation.GameId,
                operation.PlayerId,
                operation.GameInput,
                terms,
                Now),
            CancellationToken.None);
        Assert.Equal(WagerOperationStatus.Reserving, (await store.GetAsync("op-1", CancellationToken.None))!.Status);
        Assert.IsType<LedgerHoldRequested>(Assert.Single(commands.Sent));

        await coordinator.HandleAsync(
            new LedgerOperationCompleted(
                "op-1", LedgerOperationKind.Hold, LedgerOperationStatus.Held, true,
                "player-1", "op-1", 10, 10, "coins", null, Now),
            CancellationToken.None);
        Assert.Equal(WagerOperationStatus.Playing, (await store.GetAsync("op-1", CancellationToken.None))!.Status);
        Assert.IsType<BlackjackWagerStart>(commands.Sent[1]);

        var gameDecision = new BlackjackWagerStandAction().Decide(
            Input(
                new BlackjackWagerStand(
                    "op-1", "bet-1", "player-1", 42, "player", 1, "stand-1", Now),
                ActiveState()));
        var domainOutcome = Assert.IsType<BlackjackWagerOutcomeDeclared>(Assert.Single(gameDecision.Events));
        await coordinator.HandleAsync(
            new GameOutcomeDeclared(
                domainOutcome.BetId,
                "blackjack",
                domainOutcome.PlayerId,
                domainOutcome.OutcomeCode,
                domainOutcome.Evidence,
                DateTimeOffset.FromUnixTimeMilliseconds(domainOutcome.OccurredAt)),
            CancellationToken.None);
        await coordinator.HandleAsync(
            new GameOutcomeDeclared(
                domainOutcome.BetId,
                "blackjack",
                domainOutcome.PlayerId,
                domainOutcome.OutcomeCode,
                domainOutcome.Evidence,
                DateTimeOffset.FromUnixTimeMilliseconds(domainOutcome.OccurredAt)),
            CancellationToken.None);

        Assert.Equal(WagerOperationStatus.Settling, (await store.GetAsync("op-1", CancellationToken.None))!.Status);
        Assert.Equal(3, commands.Sent.Count);
        var capture = Assert.IsType<LedgerCaptureRequested>(commands.Sent[2]);
        Assert.Equal("op-1:capture", capture.OperationId);
        Assert.Equal(10, capture.Amount);

        await coordinator.HandleAsync(
            new LedgerOperationCompleted(
                "op-1:capture", LedgerOperationKind.Capture, LedgerOperationStatus.Captured, true,
                "player-1", "op-1", 10, 0, "coins", null, Now),
            CancellationToken.None);
        await coordinator.HandleAsync(
            new LedgerOperationCompleted(
                "op-1:capture", LedgerOperationKind.Capture, LedgerOperationStatus.Captured, true,
                "player-1", "op-1", 10, 0, "coins", null, Now),
            CancellationToken.None);

        var transfer = Assert.IsType<LedgerTransferRequested>(commands.Sent[3]);
        Assert.Equal("op-1:payout", transfer.OperationId);
        Assert.Equal(20, transfer.Amount);

        await coordinator.HandleAsync(
            new LedgerOperationCompleted(
                "op-1:payout", LedgerOperationKind.Transfer, LedgerOperationStatus.Completed, true,
                "house", "player-1", 20, null, "coins", null, Now),
            CancellationToken.None);
        await coordinator.HandleAsync(
            new LedgerOperationCompleted(
                "op-1:payout", LedgerOperationKind.Transfer, LedgerOperationStatus.Completed, true,
                "house", "player-1", 20, null, "coins", null, Now),
            CancellationToken.None);

        Assert.Equal(WagerOperationStatus.Completed, (await store.GetAsync("op-1", CancellationToken.None))!.Status);
        Assert.Single(events.Published);
        Assert.Equal(WagerOperationStatus.Completed, events.Published[0].Status);
    }

    private static BlackjackWagerState ActiveState() =>
        new(
            1,
            "bet-1",
            "player-1",
            42,
            "player",
            TurnGameStatus.Active,
            "player-1",
            Now.AddMinutes(1),
            ["KS", "KH"],
            ["8S", "5H"],
            "4S",
            null,
            null,
            null);

    private static GameActionInput<BlackjackWagerState, TCommand> Input<TCommand>(
        TCommand command,
        BlackjackWagerState state) =>
        new(
            command,
            state,
            new WalletSnapshot(0),
            new Dictionary<string, QuotaSnapshot>(),
            EntropyValue.Empty,
            Now);

    private sealed class MemoryWagerStore : IWagerOperationStore
    {
        private readonly Dictionary<string, WagerOperation> operations = new(StringComparer.Ordinal);

        public Task<WagerOperation> CreateOrGetAsync(WagerOperation operation, CancellationToken ct)
        {
            if (!operations.TryGetValue(operation.OperationId, out var existing))
                operations.Add(operation.OperationId, operation);
            return Task.FromResult(existing ?? operation);
        }

        public Task<WagerOperation?> GetAsync(string operationId, CancellationToken ct) =>
            Task.FromResult(operations.GetValueOrDefault(operationId));

        public Task<WagerOperation?> GetByBetIdAsync(string betId, CancellationToken ct) =>
            Task.FromResult(operations.Values.SingleOrDefault(operation => operation.BetId == betId));

        public Task<WagerOperation> TransitionAsync(
            string operationId,
            WagerOperationStatus status,
            string? outcomeCode,
            string? errorCode,
            CancellationToken ct)
        {
            var current = operations[operationId];
            var updated = current with
            {
                Status = status,
                OutcomeCode = outcomeCode ?? current.OutcomeCode,
                ErrorCode = errorCode,
                UpdatedAt = Now,
            };
            operations[operationId] = updated;
            return Task.FromResult(updated);
        }

        public async Task<WagerOperation?> TryTransitionAsync(
            string operationId,
            WagerOperationStatus expectedStatus,
            WagerOperationStatus status,
            string? outcomeCode,
            string? errorCode,
            CancellationToken ct,
            long? payout = null)
        {
            var current = operations.GetValueOrDefault(operationId);
            if (current is null || current.Status != expectedStatus)
                return null;
            var updated = await TransitionAsync(operationId, status, outcomeCode, errorCode, ct);
            if (payout is null)
                return updated;
            updated = updated with { Payout = payout, UpdatedAt = Now };
            operations[operationId] = updated;
            return updated;
        }
    }

    private sealed class RecordingCommandPublisher : IIntegrationCommandPublisher
    {
        public List<IIntegrationCommand> Sent { get; } = [];

        public Task SendAsync<TCommand>(TCommand command, CancellationToken ct)
            where TCommand : IIntegrationCommand
        {
            Sent.Add(command);
            return Task.CompletedTask;
        }

        public Task SendAsync(IIntegrationCommand command, CancellationToken ct)
        {
            Sent.Add(command);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingEventPublisher : IIntegrationEventPublisher
    {
        public List<WagerSettled> Published { get; } = [];

        public Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken ct)
            where TEvent : IIntegrationEvent
        {
            if (integrationEvent is WagerSettled settled)
                Published.Add(settled);
            return Task.CompletedTask;
        }
    }
}

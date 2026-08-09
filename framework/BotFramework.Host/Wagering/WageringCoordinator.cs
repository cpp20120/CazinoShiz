using System.Text.Json;
using BotFramework.Contracts.Messaging;
using BotFramework.Contracts.Wagering;

namespace BotFramework.Host.Wagering;

/// <summary>Coordinates wager stages without applying funds or interpreting outcomes.</summary>
public sealed class WageringCoordinator(
    IWagerOperationStore operations,
    IIntegrationCommandPublisher commands,
    IEnumerable<IWagerGameCommandFactory> games,
    IEnumerable<IWagerSettlementCommandFactory> settlements,
    IIntegrationEventPublisher events)
    : IIntegrationCommandHandler<WagerRequested>, IIntegrationEventHandler<LedgerReservationCompleted>,
      IIntegrationEventHandler<GameOutcomeDeclared>, IIntegrationEventHandler<LedgerSettlementCompleted>
{
    public async Task HandleAsync(WagerRequested wager, CancellationToken ct)
    {
        var operation = await operations.CreateOrGetAsync(new WagerOperation(
            wager.OperationId, wager.BetId, wager.GameId, wager.PlayerId, wager.GameInput,
            JsonSerializer.Serialize(wager.Terms), WagerOperationStatus.Pending, null, null,
            wager.OccurredAt, wager.OccurredAt), ct);
        if (operation.Status != WagerOperationStatus.Pending) return;
        if (await operations.TryTransitionAsync(
                wager.OperationId,
                WagerOperationStatus.Pending,
                WagerOperationStatus.Reserving,
                null,
                null,
                ct) is null)
            return;
        await commands.SendAsync(new LedgerReservationRequested(wager.OperationId, wager.BetId, wager.PlayerId,
            wager.Terms.Stake, wager.Terms.Currency, wager.OccurredAt), ct);
    }

    public async Task HandleAsync(LedgerReservationCompleted result, CancellationToken ct)
    {
        var operation = await operations.GetByBetIdAsync(result.BetId, ct);
        if (operation is null
            || !string.Equals(operation.OperationId, result.OperationId, StringComparison.Ordinal)
            || operation.Status != WagerOperationStatus.Reserving)
            return;
        if (!result.Reserved)
        {
            await operations.TryTransitionAsync(
                operation.OperationId,
                WagerOperationStatus.Reserving,
                WagerOperationStatus.Rejected,
                null,
                result.RejectionCode,
                ct);
            return;
        }
        var terms = JsonSerializer.Deserialize<WagerTermsSnapshot>(operation.TermsJson)
            ?? throw new InvalidOperationException($"Wager '{operation.OperationId}' has invalid terms.");
        var factory = games.SingleOrDefault(game => string.Equals(game.GameId, operation.GameId, StringComparison.Ordinal))
            ?? games.SingleOrDefault(game => string.Equals(game.GameId, "*", StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"No outcome-only game adapter is registered for '{operation.GameId}'.");
        if (await operations.TryTransitionAsync(
                operation.OperationId,
                WagerOperationStatus.Reserving,
                WagerOperationStatus.Playing,
                null,
                null,
                ct) is null)
            return;
        await commands.SendAsync(factory.Create(new WagerRequested(operation.OperationId, operation.BetId,
            operation.GameId, operation.PlayerId, operation.GameInput, terms, result.OccurredAt)), ct);
    }

    public async Task HandleAsync(GameOutcomeDeclared outcome, CancellationToken ct)
    {
        var operation = await operations.GetByBetIdAsync(outcome.BetId, ct);
        if (operation is null
            || !string.Equals(operation.GameId, outcome.GameId, StringComparison.Ordinal)
            || !string.Equals(operation.PlayerId, outcome.PlayerId, StringComparison.Ordinal)
            || operation.Status != WagerOperationStatus.Playing)
            return;
        var terms = JsonSerializer.Deserialize<WagerTermsSnapshot>(operation.TermsJson)
            ?? throw new InvalidOperationException($"Wager '{operation.OperationId}' has invalid terms.");
        var factory = settlements.SingleOrDefault(x => string.Equals(x.GameId, operation.GameId, StringComparison.Ordinal))
            ?? settlements.SingleOrDefault(x => string.Equals(x.GameId, "*", StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"No settlement policy is registered for '{operation.GameId}'.");
        if (await operations.TryTransitionAsync(
                operation.OperationId,
                WagerOperationStatus.Playing,
                WagerOperationStatus.Settling,
                outcome.OutcomeCode,
                null,
                ct) is null)
            return;
        await commands.SendAsync(factory.Create(operation, outcome, terms), ct);
    }

    public async Task HandleAsync(LedgerSettlementCompleted settlement, CancellationToken ct)
    {
        var operation = await operations.GetByBetIdAsync(settlement.BetId, ct);
        if (operation is null
            || !string.Equals(operation.OperationId, settlement.OperationId, StringComparison.Ordinal)
            || operation.Status != WagerOperationStatus.Settling)
            return;
        var status = settlement.Settled ? WagerOperationStatus.Completed : WagerOperationStatus.Failed;
        var updated = await operations.TryTransitionAsync(
            operation.OperationId,
            WagerOperationStatus.Settling,
            status,
            operation.OutcomeCode,
            settlement.ErrorCode,
            ct);
        if (updated is null) return;
        await events.PublishAsync(new WagerSettled(updated.OperationId, updated.BetId, updated.PlayerId,
            updated.Status, updated.OutcomeCode ?? string.Empty, settlement.OccurredAt), ct);
    }
}

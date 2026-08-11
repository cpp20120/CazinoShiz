using System.Text.Json;
using BotFramework.Contracts.Ledger;
using BotFramework.Contracts.Messaging;
using BotFramework.Contracts.Wagering;

namespace BotFramework.Host.Wagering;

/// <summary>
/// Coordinates the game/economics boundary as a durable saga. Game commands
/// never touch a wallet; Ledger owns the hold and settlement invariants.
/// </summary>
public sealed class WageringCoordinator(
    IWagerOperationStore operations,
    IIntegrationCommandPublisher commands,
    IEnumerable<IWagerGameCommandFactory> games,
    IEnumerable<IWagerSettlementCommandFactory> settlements,
    IIntegrationEventPublisher events)
    : IIntegrationCommandHandler<WagerRequested>,
      IIntegrationEventHandler<LedgerOperationCompleted>,
      IIntegrationEventHandler<GameOutcomeDeclared>
{
    private const string HouseAccountId = "house";

    public async Task HandleAsync(WagerRequested wager, CancellationToken ct)
    {
        var operation = await operations.CreateOrGetAsync(new WagerOperation(
            wager.OperationId, wager.BetId, wager.GameId, wager.PlayerId, wager.GameInput,
            JsonSerializer.Serialize(wager.Terms), WagerOperationStatus.Pending, null, null,
            wager.OccurredAt, wager.OccurredAt), ct);
        if (operation.Status != WagerOperationStatus.Pending)
            return;

        if (await operations.TryTransitionAsync(
                wager.OperationId,
                WagerOperationStatus.Pending,
                WagerOperationStatus.Reserving,
                null,
                null,
                ct) is null)
            return;

        await commands.SendAsync(new LedgerHoldRequested(
            wager.OperationId,
            wager.OperationId,
            wager.PlayerId,
            wager.Terms.Stake,
            wager.Terms.Currency,
            wager.OccurredAt.AddMinutes(15),
            "wager.hold",
            wager.OccurredAt), ct);
    }

    public async Task HandleAsync(LedgerOperationCompleted result, CancellationToken ct)
    {
        if (!TryResolveWagerOperationId(result, out var operationId))
            return;

        var operation = await operations.GetAsync(operationId, ct);
        if (operation is null)
            return;

        switch (result.OperationKind)
        {
            case LedgerOperationKind.Hold:
                await HandleHoldCompletedAsync(operation, result, ct);
                break;
            case LedgerOperationKind.Capture:
                await HandleCaptureCompletedAsync(operation, result, ct);
                break;
            case LedgerOperationKind.Transfer:
                await HandlePayoutCompletedAsync(operation, result, ct);
                break;
        }
    }

    private async Task HandleHoldCompletedAsync(
        WagerOperation operation,
        LedgerOperationCompleted result,
        CancellationToken ct)
    {
        if (operation.Status != WagerOperationStatus.Reserving
            || !string.Equals(result.OperationId, operation.OperationId, StringComparison.Ordinal))
            return;

        if (!result.Succeeded)
        {
            await operations.TryTransitionAsync(
                operation.OperationId,
                WagerOperationStatus.Reserving,
                WagerOperationStatus.Rejected,
                null,
                result.ErrorCode,
                ct);
            return;
        }

        var terms = DeserializeTerms(operation);
        var factory = FindGameFactory(operation.GameId);
        if (await operations.TryTransitionAsync(
                operation.OperationId,
                WagerOperationStatus.Reserving,
                WagerOperationStatus.Playing,
                null,
                null,
                ct) is null)
            return;

        await commands.SendAsync(factory.Create(new WagerRequested(
            operation.OperationId,
            operation.BetId,
            operation.GameId,
            operation.PlayerId,
            operation.GameInput,
            terms,
            result.OccurredAt)), ct);
    }

    public async Task HandleAsync(GameOutcomeDeclared outcome, CancellationToken ct)
    {
        var operation = await operations.GetByBetIdAsync(outcome.BetId, ct);
        if (operation is null
            || !string.Equals(operation.GameId, outcome.GameId, StringComparison.Ordinal)
            || !string.Equals(operation.PlayerId, outcome.PlayerId, StringComparison.Ordinal)
            || operation.Status != WagerOperationStatus.Playing)
            return;

        var terms = DeserializeTerms(operation);
        var factory = settlements.SingleOrDefault(x =>
                          string.Equals(x.GameId, operation.GameId, StringComparison.Ordinal))
                      ?? settlements.SingleOrDefault(x => string.Equals(x.GameId, "*", StringComparison.Ordinal))
                      ?? throw new InvalidOperationException(
                          $"No settlement policy is registered for '{operation.GameId}'.");
        var plan = factory.Create(operation, outcome, terms);
        if (plan.Payout < 0)
            throw new InvalidOperationException($"Payout for '{operation.GameId}' cannot be negative.");

        if (await operations.TryTransitionAsync(
                operation.OperationId,
                WagerOperationStatus.Playing,
                WagerOperationStatus.Settling,
                outcome.OutcomeCode,
                null,
                ct,
                plan.Payout) is null)
            return;

        await commands.SendAsync(new LedgerCaptureRequested(
            $"{operation.OperationId}:capture",
            operation.OperationId,
            operation.PlayerId,
            terms.Stake,
            terms.Currency,
            "wager.capture",
            outcome.OccurredAt,
            HouseAccountId), ct);
    }

    private async Task HandleCaptureCompletedAsync(
        WagerOperation operation,
        LedgerOperationCompleted result,
        CancellationToken ct)
    {
        if (operation.Status != WagerOperationStatus.Settling
            || !string.Equals(result.OperationId, $"{operation.OperationId}:capture", StringComparison.Ordinal))
            return;

        if (!result.Succeeded)
        {
            await CompleteAsync(operation, WagerOperationStatus.Failed, result.ErrorCode, result.OccurredAt, ct);
            return;
        }

        if (operation.Payout.GetValueOrDefault() == 0)
        {
            await CompleteAsync(operation, WagerOperationStatus.Completed, null, result.OccurredAt, ct);
            return;
        }

        var terms = DeserializeTerms(operation);
        await commands.SendAsync(new LedgerTransferRequested(
            $"{operation.OperationId}:payout",
            HouseAccountId,
            operation.PlayerId,
            operation.Payout!.Value,
            terms.Currency,
            "wager.payout",
            result.OccurredAt), ct);
    }

    private async Task HandlePayoutCompletedAsync(
        WagerOperation operation,
        LedgerOperationCompleted result,
        CancellationToken ct)
    {
        if (operation.Status != WagerOperationStatus.Settling
            || !string.Equals(result.OperationId, $"{operation.OperationId}:payout", StringComparison.Ordinal))
            return;

        await CompleteAsync(
            operation,
            result.Succeeded ? WagerOperationStatus.Completed : WagerOperationStatus.Failed,
            result.ErrorCode,
            result.OccurredAt,
            ct);
    }

    private async Task CompleteAsync(
        WagerOperation operation,
        WagerOperationStatus status,
        string? errorCode,
        DateTimeOffset occurredAt,
        CancellationToken ct)
    {
        var updated = await operations.TryTransitionAsync(
            operation.OperationId,
            WagerOperationStatus.Settling,
            status,
            operation.OutcomeCode,
            errorCode,
            ct);
        if (updated is null)
            return;

        await events.PublishAsync(new WagerSettled(
            updated.OperationId,
            updated.BetId,
            updated.PlayerId,
            updated.Status,
            updated.OutcomeCode ?? string.Empty,
            occurredAt), ct);
    }

    private IWagerGameCommandFactory FindGameFactory(string gameId) =>
        games.SingleOrDefault(game => string.Equals(game.GameId, gameId, StringComparison.Ordinal))
        ?? games.SingleOrDefault(game => string.Equals(game.GameId, "*", StringComparison.Ordinal))
        ?? throw new InvalidOperationException($"No outcome-only game adapter is registered for '{gameId}'.");

    private static WagerTermsSnapshot DeserializeTerms(WagerOperation operation) =>
        JsonSerializer.Deserialize<WagerTermsSnapshot>(operation.TermsJson)
        ?? throw new InvalidOperationException($"Wager '{operation.OperationId}' has invalid terms.");

    private static bool TryResolveWagerOperationId(
        LedgerOperationCompleted result,
        out string operationId)
    {
        var suffix = result.OperationKind switch
        {
            LedgerOperationKind.Hold => null,
            LedgerOperationKind.Capture => ":capture",
            LedgerOperationKind.Transfer => ":payout",
            _ => null,
        };
        if (suffix is null)
        {
            operationId = result.OperationId;
            return result.OperationKind == LedgerOperationKind.Hold;
        }

        if (!result.OperationId.EndsWith(suffix, StringComparison.Ordinal))
        {
            operationId = string.Empty;
            return false;
        }

        operationId = result.OperationId[..^suffix.Length];
        return !string.IsNullOrWhiteSpace(operationId);
    }
}

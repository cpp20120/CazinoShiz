using BotFramework.Contracts.Ledger;
using BotFramework.Contracts.Messaging;
using BotFramework.Contracts.Tenancy;
using BotFramework.Contracts.Wagering;

namespace BotFramework.Host.Wagering;

public sealed class MultiPartyWagerWorkflowExecutor(
    PostgresMultiPartyWagerStore store,
    IIntegrationCommandPublisher commands,
    IEnumerable<IMultiPartyWagerGameAdapter> adapters,
    IEnumerable<IMultiPartyWagerPayoutPolicy> payoutPolicies,
    ITenantContextAccessor tenantContext)
{
    public async Task<MultiPartyWagerResult> ExecuteAsync(
        MultiPartyWagerWorkflowCommand command,
        CancellationToken ct)
    {
        var request = command.Request;
        using var context = PushTenant(request, command.CommandId);
        await store.EnsureCreatedAsync(request, ct);

        var participants = request.Participants;
        foreach (var participant in participants)
        {
            await commands.SendAsync(new LedgerHoldRequested(
                participant.OperationId,
                participant.OperationId,
                participant.PlayerId,
                participant.Stake,
                participant.Currency,
                request.OccurredAt.AddMinutes(15),
                "multiparty.wager.hold",
                request.OccurredAt), ct);

            var status = await store.GetReservationStatusAsync(participant.OperationId, ct);
            if (status is null or "processing")
                throw new InvalidOperationException("multiparty_reservation_pending");
            if (!string.Equals(status, "reserved", StringComparison.Ordinal)
                && !string.Equals(status, "held", StringComparison.Ordinal))
            {
                await store.SetParticipantStatusAsync(request.WorkflowId, participant.BetId, "rejected", null, null, "participant_reservation_rejected", ct);
                await CompensateAsync(request, participants, ct);
                return new(request.WorkflowId, "compensated", [], "participant_reservation_rejected");
            }
            await store.SetParticipantStatusAsync(request.WorkflowId, participant.BetId, "reserved", null, null, null, ct);
        }

        await store.SetGroupStatusAsync(request.WorkflowId, "playing", null, null, ct);
        IReadOnlyList<MultiPartyWagerOutcome> outcomes;
        try
        {
            var adapter = adapters.SingleOrDefault(x => string.Equals(x.GameId, request.GameId, StringComparison.Ordinal))
                ?? throw new InvalidOperationException($"No multiparty game adapter is registered for '{request.GameId}'.");
            var gameOutcomes = await adapter.ExecuteAsync(
                new MultiPartyWagerGameRequest(
                    request.WorkflowId,
                    request.GameId,
                    request.GameInput,
                    participants.Select(x => new MultiPartyWagerGameParticipant(x.BetId, x.PlayerId)).ToArray()),
                ct);
            ValidateGameOutcomes(participants, gameOutcomes);
            var payoutPolicy = payoutPolicies.SingleOrDefault(x => string.Equals(x.GameId, request.GameId, StringComparison.Ordinal))
                ?? throw new InvalidOperationException($"No multiparty payout policy is registered for '{request.GameId}'.");
            outcomes = payoutPolicy.Calculate(request, gameOutcomes);
            ValidateOutcomes(participants, outcomes);
        }
        catch
        {
            await CompensateAsync(request, participants, ct);
            return new(request.WorkflowId, "compensated", [], "game_execution_failed");
        }

        await store.SetGroupStatusAsync(request.WorkflowId, "settling", null, outcomes, ct);
        foreach (var outcome in outcomes)
        {
            var participant = participants.Single(x => string.Equals(x.BetId, outcome.BetId, StringComparison.Ordinal));
            await commands.SendAsync(new LedgerCaptureRequested(
                $"{participant.OperationId}:capture",
                participant.OperationId,
                participant.PlayerId,
                participant.Stake,
                participant.Currency,
                "multiparty.wager.capture",
                request.OccurredAt,
                "house"), ct);

            var captureStatus = await store.GetLedgerOperationStatusAsync($"{participant.OperationId}:capture", ct);
            if (captureStatus is null or "processing")
                throw new InvalidOperationException("multiparty_settlement_pending");
            if (!string.Equals(captureStatus, "captured", StringComparison.Ordinal)
                && !string.Equals(captureStatus, "partially_captured", StringComparison.Ordinal))
            {
                await store.SetParticipantStatusAsync(request.WorkflowId, participant.BetId, "failed", outcome.OutcomeCode, outcome.Payout, "settlement_rejected", ct);
                return new(request.WorkflowId, "failed", outcomes, "settlement_rejected");
            }

            if (outcome.Payout > 0)
            {
                await commands.SendAsync(new LedgerTransferRequested(
                    $"{participant.OperationId}:payout",
                    "house",
                    participant.PlayerId,
                    outcome.Payout,
                    participant.Currency,
                    "multiparty.wager.payout",
                    request.OccurredAt), ct);
                var payoutStatus = await store.GetLedgerOperationStatusAsync($"{participant.OperationId}:payout", ct);
                if (!string.Equals(payoutStatus, "completed", StringComparison.Ordinal))
                {
                    await store.SetParticipantStatusAsync(request.WorkflowId, participant.BetId, "failed", outcome.OutcomeCode, outcome.Payout, "payout_rejected", ct);
                    return new(request.WorkflowId, "failed", outcomes, "payout_rejected");
                }
            }
            await store.SetParticipantStatusAsync(request.WorkflowId, participant.BetId, "settled", outcome.OutcomeCode, outcome.Payout, null, ct);
        }

        await store.SetGroupStatusAsync(request.WorkflowId, "completed", null, outcomes, ct);
        return new(request.WorkflowId, "completed", outcomes);
    }

    private async Task CompensateAsync(
        MultiPartyWagerRequest request,
        IReadOnlyList<MultiPartyWagerParticipant> participants,
        CancellationToken ct)
    {
        await store.SetGroupStatusAsync(request.WorkflowId, "compensating", "participant_reservation_rejected", null, ct);
        foreach (var participant in participants)
        {
            var status = await store.GetReservationStatusAsync(participant.OperationId, ct);
            if (status is null or "processing")
                throw new InvalidOperationException("multiparty_compensation_pending");
            if (string.Equals(status, "rejected", StringComparison.Ordinal)
                || string.Equals(status, "refunded", StringComparison.Ordinal))
                continue;
            if (!string.Equals(status, "reserved", StringComparison.Ordinal))
                throw new InvalidOperationException($"Cannot compensate reservation in status '{status}'.");

            await store.SetParticipantStatusAsync(request.WorkflowId, participant.BetId, "refunding", null, null, null, ct);
            await commands.SendAsync(new LedgerReleaseRequested(
                $"{participant.OperationId}:release",
                participant.OperationId,
                participant.PlayerId,
                participant.Stake,
                participant.Currency,
                "multiparty.wager.release",
                request.OccurredAt), ct);
            var refundStatus = await store.GetLedgerOperationStatusAsync($"{participant.OperationId}:release", ct);
            if (string.Equals(refundStatus, "released", StringComparison.Ordinal)
                || string.Equals(refundStatus, "partially_released", StringComparison.Ordinal))
                await store.SetParticipantStatusAsync(request.WorkflowId, participant.BetId, "refunded", "refund", participant.Stake, null, ct);
            else
                throw new InvalidOperationException("multiparty_refund_pending");
        }
        await store.SetGroupStatusAsync(request.WorkflowId, "compensated", "participant_reservation_rejected", null, ct);
    }

    private IDisposable PushTenant(MultiPartyWagerRequest request, string commandId)
    {
        var context = TenantContext.Create(
            TenantId.Create(request.TenantId),
            ScopeId.Create(request.ScopeId),
            null,
            BotChannel.System,
            RequestId.Create(commandId),
            RequestId.Create(request.WorkflowId));
        var metadata = RequestMetadataContext.Push(RequestMetadata.FromTenantContext(context, "multiparty-wager"));
        var tenant = tenantContext.Push(context);
        return new CompositeScope(metadata, tenant);
    }

    private static void ValidateOutcomes(
        IReadOnlyList<MultiPartyWagerParticipant> participants,
        IReadOnlyList<MultiPartyWagerOutcome> outcomes)
    {
        if (outcomes.Count != participants.Count
            || outcomes.Select(x => x.BetId).Distinct(StringComparer.Ordinal).Count() != participants.Count
            || outcomes.Any(x => x.Payout < 0 || !participants.Any(p => p.BetId == x.BetId && p.PlayerId == x.PlayerId)))
            throw new InvalidOperationException("multiparty_outcome_conflict");
    }

    private static void ValidateGameOutcomes(
        IReadOnlyList<MultiPartyWagerParticipant> participants,
        IReadOnlyList<MultiPartyWagerGameOutcome> outcomes)
    {
        if (outcomes.Count != participants.Count
            || outcomes.Select(x => x.BetId).Distinct(StringComparer.Ordinal).Count() != participants.Count
            || outcomes.Any(x => !participants.Any(p => p.BetId == x.BetId && p.PlayerId == x.PlayerId)))
            throw new InvalidOperationException("multiparty_game_outcome_conflict");
    }

    private sealed class CompositeScope(IDisposable first, IDisposable second) : IDisposable
    {
        public void Dispose()
        {
            second.Dispose();
            first.Dispose();
        }
    }
}

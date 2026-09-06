namespace BotFramework.Sdk.Execution.DeferredOutcome;

public sealed class DeferredOutcomeWagerResolveAction<TGame, TOutcome>(
    DeferredOutcomeWagerDefinition<TGame, TOutcome> definition)
    : IGameAction<
        DeferredOutcomeWagerResolveCommand<TGame, TOutcome>,
        DeferredOutcomeWagerState,
        DeferredOutcomeWagerResolveResult<TOutcome>>
    where TGame : IDeferredOutcomeWagerGame
{
    public GameDecision<DeferredOutcomeWagerState, DeferredOutcomeWagerResolveResult<TOutcome>> Decide(
        GameActionInput<DeferredOutcomeWagerState, DeferredOutcomeWagerResolveCommand<TGame, TOutcome>> input)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (input.State.PendingWager is not { } wager)
        {
            return Reject(
                input.State,
                new(DeferredOutcomeWagerResolveStatus.NoPendingWager, input.Command.Outcome, 0, 0, input.Wallet.Balance),
                "no_pending_wager");
        }
        if (!Matches(wager, input.Command))
        {
            return Reject(
                input.State,
                new(DeferredOutcomeWagerResolveStatus.PendingWagerMismatch, input.Command.Outcome, 0, 0, input.Wallet.Balance),
                "pending_wager_mismatch");
        }

        var payout = definition.CalculatePayout(wager, input.Command.Outcome);
        if (payout < 0)
            throw new InvalidOperationException("A deferred-outcome wager payout cannot be negative.");

        var quotaUsage = RequiredQuotaUsage(input.Quotas);
        return new(
            DecisionStatus.Accepted,
            input.State.WithPendingWager(null),
            new(
                DeferredOutcomeWagerResolveStatus.Resolved,
                input.Command.Outcome,
                wager.Amount,
                payout,
                checked(input.Wallet.Balance + payout),
                quotaUsage),
            payout > 0 ? [EconomyEffect.Credit(payout, definition.PayoutReason)] : [],
            [],
            [],
            definition.ResolvedEvents(new DeferredOutcomeWagerResolved<TOutcome>(
                wager,
                input.Command.DisplayName,
                input.Command.Outcome,
                payout,
                input.UtcNow,
                input.Entropy,
                input.Channel,
                input.Command.CommandId)),
            []);
    }

    private DeferredOutcomeWagerQuotaUsage? RequiredQuotaUsage(IReadOnlyDictionary<string, QuotaSnapshot> quotas)
    {
        if (definition.DailyQuota is null)
            return null;

        return quotas.TryGetValue(definition.DailyQuota.Id, out var quota)
            ? new DeferredOutcomeWagerQuotaUsage(quota.Used, quota.Limit)
            : throw new InvalidOperationException($"Required quota '{definition.DailyQuota.Id}' was not supplied.");
    }

    private static bool Matches(
        DeferredOutcomeWager wager,
        DeferredOutcomeWagerResolveCommand<TGame, TOutcome> command) =>
        wager.UserId == command.UserId && wager.ChatId == command.ChatId;

    private static GameDecision<DeferredOutcomeWagerState, DeferredOutcomeWagerResolveResult<TOutcome>> Reject(
        DeferredOutcomeWagerState state,
        DeferredOutcomeWagerResolveResult<TOutcome> result,
        string reason) =>
        new(DecisionStatus.Rejected, state, result, [], [], [], [], [], reason);
}

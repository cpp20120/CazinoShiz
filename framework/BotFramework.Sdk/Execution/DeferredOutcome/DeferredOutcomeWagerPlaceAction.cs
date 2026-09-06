namespace BotFramework.Sdk.Execution.DeferredOutcome;

public sealed class DeferredOutcomeWagerPlaceAction<TGame, TOutcome>(
    DeferredOutcomeWagerDefinition<TGame, TOutcome> definition)
    : IGameAction<
        DeferredOutcomeWagerPlaceCommand<TGame, TOutcome>,
        DeferredOutcomeWagerState,
        DeferredOutcomeWagerPlaceResult>
    where TGame : IDeferredOutcomeWagerGame
{
    public GameDecision<DeferredOutcomeWagerState, DeferredOutcomeWagerPlaceResult> Decide(
        GameActionInput<DeferredOutcomeWagerState, DeferredOutcomeWagerPlaceCommand<TGame, TOutcome>> input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var command = input.Command;
        var quota = RequiredQuota(input.Quotas);
        var quotaUsage = ToUsage(quota);
        if (command.Amount <= 0 || command.Amount > command.MaximumAmount)
            return Reject(input.State, new(DeferredOutcomeWagerPlaceStatus.InvalidAmount, input.Wallet.Balance, Quota: quotaUsage), "invalid_amount");
        if (command.BlockingGameId is not null)
        {
            return Reject(
                input.State,
                new(DeferredOutcomeWagerPlaceStatus.BusyOtherGame, input.Wallet.Balance, command.Amount, command.BlockingGameId, quotaUsage),
                "busy_other_game");
        }
        if (input.State.PendingWager is { } pending)
        {
            return Reject(
                input.State,
                new(DeferredOutcomeWagerPlaceStatus.AlreadyPending, input.Wallet.Balance, pending.Amount, Quota: quotaUsage),
                "already_pending");
        }
        if (quota is { Limit: > 0, Used: var used } && used >= quota.Limit)
        {
            return Reject(
                input.State,
                new(DeferredOutcomeWagerPlaceStatus.DailyQuotaExceeded, input.Wallet.Balance, Quota: quotaUsage),
                "daily_quota_exceeded");
        }
        if (command.Amount > input.Wallet.Balance)
        {
            return Reject(
                input.State,
                new(DeferredOutcomeWagerPlaceStatus.InsufficientBalance, input.Wallet.Balance, Quota: quotaUsage),
                "insufficient_balance");
        }

        var wager = new DeferredOutcomeWager(command.UserId, command.ChatId, command.Amount, input.UtcNow);
        var updatedQuota = quota is null ? null : UpdatedQuotaUsage(quota);
        return new(
            DecisionStatus.Accepted,
            input.State.WithPendingWager(wager),
            new DeferredOutcomeWagerPlaceResult(
                DeferredOutcomeWagerPlaceStatus.Accepted,
                checked(input.Wallet.Balance - command.Amount),
                wager.Amount,
                Quota: updatedQuota),
            [EconomyEffect.Debit(command.Amount, definition.DebitReason)],
            quota is { Limit: > 0 } ? [QuotaEffect.Consume(definition.DailyQuota!.Id)] : [],
            [],
            definition.PlacedEvents(new DeferredOutcomeWagerPlaced(wager, command.DisplayName, input.UtcNow, input.Channel)),
            []);
    }

    private QuotaSnapshot? RequiredQuota(IReadOnlyDictionary<string, QuotaSnapshot> quotas)
    {
        if (definition.DailyQuota is null)
            return null;

        return quotas.TryGetValue(definition.DailyQuota.Id, out var quota)
            ? quota
            : throw new InvalidOperationException($"Required quota '{definition.DailyQuota.Id}' was not supplied.");
    }

    private static DeferredOutcomeWagerQuotaUsage? ToUsage(QuotaSnapshot? quota) =>
        quota is null ? null : new(quota.Used, quota.Limit);

    private static DeferredOutcomeWagerQuotaUsage UpdatedQuotaUsage(QuotaSnapshot quota) =>
        new(quota.Limit > 0 ? checked(quota.Used + 1) : 0, quota.Limit);

    private static GameDecision<DeferredOutcomeWagerState, DeferredOutcomeWagerPlaceResult> Reject(
        DeferredOutcomeWagerState state,
        DeferredOutcomeWagerPlaceResult result,
        string reason) =>
        new(DecisionStatus.Rejected, state, result, [], [], [], [], [], reason);
}

namespace BotFramework.Sdk.Execution.InstantWager;

/// <summary>Atomic action for the standard stake, resolve, settle lifecycle.</summary>
public sealed class InstantWagerAction<TGame, TOutcome>(InstantWagerDefinition<TGame, TOutcome> definition)
    : IGameAction<InstantWagerCommand<TGame>, NoGameState, InstantWagerResult<TOutcome>>
    where TGame : IInstantWagerGame
{
    public GameDecision<NoGameState, InstantWagerResult<TOutcome>> Decide(
        GameActionInput<NoGameState, InstantWagerCommand<TGame>> input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var command = input.Command;
        var quota = RequiredQuota(input.Quotas);
        var quotaUsage = ToUsage(quota);
        if (!definition.Game.StakeLimits!.Allows(command.Amount))
        {
            return Reject(
                input,
                new(InstantWagerStatus.InvalidAmount, default!, 0, 0, input.Wallet.Balance, quotaUsage),
                "invalid_amount");
        }
        if (quota is { Limit: > 0, Used: var used } && used >= quota.Limit)
        {
            return Reject(
                input,
                new(InstantWagerStatus.DailyQuotaExceeded, default!, 0, 0, input.Wallet.Balance, quotaUsage),
                "daily_quota_exceeded");
        }
        if (command.Amount > input.Wallet.Balance)
        {
            return Reject(
                input,
                new(InstantWagerStatus.InsufficientBalance, default!, 0, 0, input.Wallet.Balance, quotaUsage),
                "insufficient_balance");
        }

        var wager = new InstantWagerSettlement(command.Context, command.Amount, input.UtcNow);
        var outcome = definition.ResolveOutcome(new(command, input.Entropy, input.UtcNow));
        var payout = definition.CalculatePayout(wager, outcome);
        if (payout < 0)
            throw new InvalidOperationException("An instant wager payout cannot be negative.");

        var balance = checked(input.Wallet.Balance - command.Amount + payout);
        var updatedQuota = quota is null ? null : UpdatedQuotaUsage(quota);
        var events = definition.Events(new(wager, outcome, payout, input.Entropy, input.Channel));
        IReadOnlyList<EconomyEffect> economy = payout > 0
            ? [EconomyEffect.Debit(command.Amount, definition.DebitReason), EconomyEffect.Credit(payout, definition.PayoutReason)]
            : [EconomyEffect.Debit(command.Amount, definition.DebitReason)];
        return new(
            DecisionStatus.Accepted,
            default,
            new(InstantWagerStatus.Accepted, outcome, command.Amount, payout, balance, updatedQuota),
            economy,
            quota is { Limit: > 0 } ? [QuotaEffect.Consume(definition.Game.DailyQuotaId!)] : [],
            [],
            events,
            []);
    }

    private QuotaSnapshot? RequiredQuota(IReadOnlyDictionary<string, QuotaSnapshot> quotas)
    {
        var quotaId = definition.Game.DailyQuotaId;
        if (quotaId is null)
            return null;

        return quotas.TryGetValue(quotaId, out var quota)
            ? quota
            : throw new InvalidOperationException($"Required quota '{quotaId}' was not supplied.");
    }

    private static InstantWagerQuotaUsage? ToUsage(QuotaSnapshot? quota) =>
        quota is null ? null : new(quota.Used, quota.Limit);

    private static InstantWagerQuotaUsage UpdatedQuotaUsage(QuotaSnapshot quota) =>
        new(quota.Limit > 0 ? checked(quota.Used + 1) : 0, quota.Limit);

    private static GameDecision<NoGameState, InstantWagerResult<TOutcome>> Reject(
        GameActionInput<NoGameState, InstantWagerCommand<TGame>> input,
        InstantWagerResult<TOutcome> result,
        string reason) =>
        new(DecisionStatus.Rejected, input.State, result, [], [], [], [], [], reason);
}

namespace BotFramework.Sdk.Execution.DeferredOutcome;

public sealed class DeferredOutcomeWagerAbortAction<TGame, TOutcome>(
    DeferredOutcomeWagerDefinition<TGame, TOutcome> definition)
    : IGameAction<
        DeferredOutcomeWagerAbortCommand<TGame, TOutcome>,
        DeferredOutcomeWagerState,
        DeferredOutcomeWagerAbortResult>
    where TGame : IDeferredOutcomeWagerGame
{
    public GameDecision<DeferredOutcomeWagerState, DeferredOutcomeWagerAbortResult> Decide(
        GameActionInput<DeferredOutcomeWagerState, DeferredOutcomeWagerAbortCommand<TGame, TOutcome>> input)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (input.State.PendingWager is not { } wager)
            return Reject(input.State, new(DeferredOutcomeWagerAbortStatus.NoPendingWager), "no_pending_wager");
        if (!Matches(wager, input.Command))
        {
            return Reject(
                input.State,
                new(DeferredOutcomeWagerAbortStatus.PendingWagerMismatch),
                "pending_wager_mismatch");
        }

        var quota = RequiredQuota(input.Quotas);
        return new(
            DecisionStatus.Accepted,
            input.State.WithPendingWager(null),
            new DeferredOutcomeWagerAbortResult(DeferredOutcomeWagerAbortStatus.Aborted),
            [EconomyEffect.Credit(wager.Amount, definition.RefundReason)],
            quota is { Limit: > 0 } ? [QuotaEffect.Restore(definition.DailyQuota!.Id)] : [],
            [],
            definition.AbortedEvents(new DeferredOutcomeWagerAborted(wager, input.Command.DisplayName, input.UtcNow, input.Channel)),
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

    private static bool Matches(
        DeferredOutcomeWager wager,
        DeferredOutcomeWagerAbortCommand<TGame, TOutcome> command) =>
        wager.UserId == command.UserId && wager.ChatId == command.ChatId;

    private static GameDecision<DeferredOutcomeWagerState, DeferredOutcomeWagerAbortResult> Reject(
        DeferredOutcomeWagerState state,
        DeferredOutcomeWagerAbortResult result,
        string reason) =>
        new(DecisionStatus.Rejected, state, result, [], [], [], [], [], reason);
}

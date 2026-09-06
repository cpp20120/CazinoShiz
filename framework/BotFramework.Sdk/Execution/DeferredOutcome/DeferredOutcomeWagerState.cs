namespace BotFramework.Sdk.Execution.DeferredOutcome;

/// <summary>
/// Framework-owned state for a single user/chat wager. Every accepted transition
/// advances <see cref="Revision"/>, so the standard JSON state store can persist it.
/// </summary>
public sealed record DeferredOutcomeWagerState(
    long Revision,
    DeferredOutcomeWager? PendingWager) : IVersionedGameState
{
    public static DeferredOutcomeWagerState Empty { get; } = new(0, null);

    public DeferredOutcomeWagerState WithPendingWager(DeferredOutcomeWager? pendingWager) =>
        new(checked(Revision + 1), pendingWager);
}

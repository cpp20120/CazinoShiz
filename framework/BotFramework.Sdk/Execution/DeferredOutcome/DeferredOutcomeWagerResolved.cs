using BotFramework.Contracts.Messaging;

namespace BotFramework.Sdk.Execution.DeferredOutcome;

/// <summary>Context exposed to a module event factory after an external outcome is settled.</summary>
public sealed record DeferredOutcomeWagerResolved<TOutcome>(
    DeferredOutcomeWager Wager,
    string DisplayName,
    TOutcome Outcome,
    long Payout,
    DateTimeOffset OccurredAt,
    EntropyValue Entropy,
    BotChannel Channel,
    string CommandId);

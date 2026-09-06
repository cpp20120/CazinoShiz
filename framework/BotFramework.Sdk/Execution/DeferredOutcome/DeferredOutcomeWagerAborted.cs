using BotFramework.Contracts.Messaging;

namespace BotFramework.Sdk.Execution.DeferredOutcome;

/// <summary>Context exposed to a module event factory after delivery compensation refunds a wager.</summary>
public sealed record DeferredOutcomeWagerAborted(
    DeferredOutcomeWager Wager,
    string DisplayName,
    DateTimeOffset OccurredAt,
    BotChannel Channel);

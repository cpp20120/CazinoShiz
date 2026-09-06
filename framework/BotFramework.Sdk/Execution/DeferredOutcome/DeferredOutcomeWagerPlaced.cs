using BotFramework.Contracts.Messaging;

namespace BotFramework.Sdk.Execution.DeferredOutcome;

/// <summary>Context exposed to a module event factory after a wager is accepted.</summary>
public sealed record DeferredOutcomeWagerPlaced(
    DeferredOutcomeWager Wager,
    string DisplayName,
    DateTimeOffset OccurredAt,
    BotChannel Channel);

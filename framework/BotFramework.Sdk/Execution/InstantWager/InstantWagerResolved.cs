using BotFramework.Contracts.Messaging;

namespace BotFramework.Sdk.Execution.InstantWager;

/// <summary>Context available to a game-specific event factory after settlement.</summary>
public sealed record InstantWagerResolved<TOutcome>(
    InstantWagerSettlement Wager,
    TOutcome Outcome,
    long Payout,
    EntropyValue Entropy,
    BotChannel Channel);

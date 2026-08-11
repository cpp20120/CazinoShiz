namespace BotFramework.Contracts.Wagering;

/// <summary>
/// Wagering-owned result of a payout policy. It is a plan, not a Ledger
/// command: the saga decides when to capture the hold and transfer the payout.
/// </summary>
public sealed record WagerSettlementPlan(long Payout);

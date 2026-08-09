namespace BotFramework.Contracts.Wagering;

/// <summary>
/// Immutable financial terms captured when a wager is accepted. Games never
/// interpret these values; only the Wagering service uses them for settlement.
/// </summary>
public sealed record WagerTermsSnapshot(
    string RulesVersion,
    long Stake,
    string Currency,
    string SettlementRule);

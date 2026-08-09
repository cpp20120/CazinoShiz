namespace BotFramework.Contracts.Wagering;

/// <summary>Game facts for one participant, ready for Wagering settlement.</summary>
public sealed record MultiPartyWagerOutcome(
    string BetId,
    string PlayerId,
    string OutcomeCode,
    long Payout,
    string Evidence);

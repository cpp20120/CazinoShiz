namespace BotFramework.Contracts.Wagering;

/// <summary>Game-owned outcome fact. It deliberately contains no payout.</summary>
public sealed record MultiPartyWagerGameOutcome(
    string BetId,
    string PlayerId,
    string OutcomeCode,
    string Evidence);

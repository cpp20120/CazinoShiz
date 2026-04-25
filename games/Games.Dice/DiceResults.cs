namespace Games.Dice;

public enum DiceOutcome
{
    /// <summary>Message was forwarded; not allowed.</summary>
    Forwarded,
    /// <summary>User doesn't have enough coins to place bet.</summary>
    NotEnoughCoins,
    /// <summary>Daily roll limit exceeded.</summary>
    DailyRollLimitExceeded,
    /// <summary>Bet was successfully placed (phase 1).</summary>
    BetPlaced,
    /// <summary>Dice roll was resolved (phase 2).</summary>
    Played,
    /// <summary>No pending bet was found to resolve.</summary>
    NoPendingBet,
    /// <summary>Failed to insert bet into store.</summary>
    BetStoreError,
}

/// <summary>Result of placing a bet (phase 1).</summary>
public sealed record DicePlaceBetResult(
    DiceOutcome Outcome,
    int Loss = 0,
    int NewBalance = 0,
    int Gas = 0,
    int DailyDiceUsed = 0,
    int DailyDiceLimit = 0);

/// <summary>Result of resolving a dice roll (phase 2).</summary>
public sealed record DiceRollResult(
    DiceOutcome Outcome,
    int Prize = 0,
    int Loss = 0,
    int NewBalance = 0);


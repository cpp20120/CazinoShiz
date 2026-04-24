namespace Games.Dice;

public enum DiceOutcome
{
    Forwarded,
    NotEnoughCoins,
    DailyRollLimitExceeded,
    Played,
}

public sealed record DicePlayResult(
    DiceOutcome Outcome,
    int Prize = 0,
    int Loss = 0,
    int NewBalance = 0,
    int Gas = 0,
    int DailyDiceUsed = 0,
    int DailyDiceLimit = 0);

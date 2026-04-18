namespace CasinoShiz.Services.Dice;

public enum DiceOutcome
{
    Forwarded,
    AttemptsLimit,
    NotEnoughCoins,
    Played,
}

public sealed record DicePlayResult(
    DiceOutcome Outcome,
    int Prize = 0,
    int Loss = 0,
    int NewBalance = 0,
    int TotalAttempts = 0,
    int MoreRolls = 0,
    int Tax = 0,
    int DaysWithoutRolls = 0,
    Guid? FreespinCode = null,
    int Gas = 0);

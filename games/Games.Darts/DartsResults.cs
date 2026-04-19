namespace Games.Darts;

public enum DartsBetError
{
    None,
    InvalidAmount,
    NotEnoughCoins,
    AlreadyPending,
}

public enum DartsThrowOutcome
{
    NoBet,
    Thrown,
}

public sealed record DartsBetResult(
    DartsBetError Error,
    int Amount = 0,
    int Balance = 0,
    int PendingAmount = 0)
{
    public static DartsBetResult Fail(DartsBetError err, int balance = 0, int pendingAmount = 0) =>
        new(err, 0, balance, pendingAmount);
}

public sealed record DartsThrowResult(
    DartsThrowOutcome Outcome,
    int Face = 0,
    int Bet = 0,
    int Multiplier = 0,
    int Payout = 0,
    int Balance = 0);

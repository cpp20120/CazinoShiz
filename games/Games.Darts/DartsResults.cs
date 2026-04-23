namespace Games.Darts;

public enum DartsBetError
{
    None,
    InvalidAmount,
    NotEnoughCoins,
    BusyOtherGame,
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
    int PendingAmount = 0,
    string? BlockingGameId = null,
    long RoundId = 0,
    int QueuedAhead = 0)
{
    public static DartsBetResult Fail(DartsBetError err, int balance = 0, int pendingAmount = 0) =>
        new(err, 0, balance, pendingAmount, null, 0, 0);
}

public sealed record DartsThrowResult(
    DartsThrowOutcome Outcome,
    int Face = 0,
    int Bet = 0,
    int Multiplier = 0,
    int Payout = 0,
    int Balance = 0);

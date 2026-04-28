namespace Games.Bowling;

public enum BowlingBetError
{
    None,
    InvalidAmount,
    NotEnoughCoins,
    AlreadyPending,
    BusyOtherGame,
    DailyRollLimit,
}

public enum BowlingRollOutcome
{
    NoBet,
    Rolled,
}

public sealed record BowlingBetResult(
    BowlingBetError Error,
    int Amount = 0,
    int Balance = 0,
    int PendingAmount = 0,
    string? BlockingGameId = null,
    int DailyRollUsed = 0,
    int DailyRollLimit = 0)
{
    public static BowlingBetResult Fail(BowlingBetError err, int balance = 0, int pendingAmount = 0) =>
        new(err, 0, balance, pendingAmount, null, 0, 0);
}

public sealed record BowlingRollResult(
    BowlingRollOutcome Outcome,
    int Face = 0,
    int Bet = 0,
    int Multiplier = 0,
    int Payout = 0,
    int Balance = 0,
    int DailyRollUsed = 0,
    int DailyRollLimit = 0);

namespace Games.Basketball;

public enum BasketballBetError
{
    None,
    InvalidAmount,
    NotEnoughCoins,
    AlreadyPending,
    BusyOtherGame,
    DailyRollLimit,
}

public enum BasketballThrowOutcome
{
    NoBet,
    Thrown,
}

public sealed record BasketballBetResult(
    BasketballBetError Error,
    int Amount = 0,
    int Balance = 0,
    int PendingAmount = 0,
    string? BlockingGameId = null,
    int DailyRollUsed = 0,
    int DailyRollLimit = 0)
{
    public static BasketballBetResult Fail(BasketballBetError err, int balance = 0, int pendingAmount = 0) =>
        new(err, 0, balance, pendingAmount, null, 0, 0);
}

public sealed record BasketballThrowResult(
    BasketballThrowOutcome Outcome,
    int Face = 0,
    int Bet = 0,
    int Multiplier = 0,
    int Payout = 0,
    int Balance = 0);

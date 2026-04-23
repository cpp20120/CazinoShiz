namespace Games.Football;

public enum FootballBetError
{
    None,
    InvalidAmount,
    NotEnoughCoins,
    AlreadyPending,
    BusyOtherGame,
}

public enum FootballThrowOutcome
{
    NoBet,
    Thrown,
}

public sealed record FootballBetResult(
    FootballBetError Error,
    int Amount = 0,
    int Balance = 0,
    int PendingAmount = 0,
    string? BlockingGameId = null)
{
    public static FootballBetResult Fail(FootballBetError err, int balance = 0, int pendingAmount = 0) =>
        new(err, 0, balance, pendingAmount, null);
}

public sealed record FootballThrowResult(
    FootballThrowOutcome Outcome,
    int Face = 0,
    int Bet = 0,
    int Multiplier = 0,
    int Payout = 0,
    int Balance = 0);

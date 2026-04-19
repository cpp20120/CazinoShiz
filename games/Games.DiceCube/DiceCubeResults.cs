namespace Games.DiceCube;

public enum CubeBetError
{
    None,
    InvalidAmount,
    NotEnoughCoins,
    AlreadyPending,
}

public enum CubeRollOutcome
{
    NoBet,
    Rolled,
}

public sealed record CubeBetResult(
    CubeBetError Error,
    int Amount = 0,
    int Balance = 0,
    int PendingAmount = 0)
{
    public static CubeBetResult Fail(CubeBetError err, int balance = 0, int pendingAmount = 0) =>
        new(err, 0, balance, pendingAmount);
}

public sealed record CubeRollResult(
    CubeRollOutcome Outcome,
    int Face = 0,
    int Bet = 0,
    int Multiplier = 0,
    int Payout = 0,
    int Balance = 0);

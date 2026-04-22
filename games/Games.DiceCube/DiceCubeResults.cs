namespace Games.DiceCube;

public enum CubeBetError
{
    None,
    InvalidAmount,
    NotEnoughCoins,
    AlreadyPending,
    Cooldown,
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
    int PendingAmount = 0,
    int CooldownSeconds = 0)
{
    public static CubeBetResult Fail(CubeBetError err, int balance = 0, int pendingAmount = 0) =>
        new(err, 0, balance, pendingAmount, 0);

    public static CubeBetResult CooldownWait(int balance, int waitSeconds) =>
        new(CubeBetError.Cooldown, 0, balance, 0, Math.Max(1, waitSeconds));
}

public sealed record CubeRollResult(
    CubeRollOutcome Outcome,
    int Face = 0,
    int Bet = 0,
    int Multiplier = 0,
    int Payout = 0,
    int Balance = 0);

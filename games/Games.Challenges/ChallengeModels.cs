namespace Games.Challenges;

public enum ChallengeGame
{
    Dice,
    DiceCube,
    Darts,
    Bowling,
    Basketball,
    Football,
    Slots,
    Horse,
    Blackjack,
}

public enum ChallengeStatus
{
    Pending,
    Accepted,
    Declined,
    Completed,
    Failed,
}

public sealed record Challenge(
    Guid Id,
    long ChatId,
    long ChallengerId,
    string ChallengerName,
    long TargetId,
    string TargetName,
    int Amount,
    ChallengeGame Game,
    ChallengeStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt);

public sealed record ChallengeUser(long UserId, string DisplayName);

public sealed record ChallengeCreateResult(
    ChallengeCreateError Error,
    Challenge? Challenge = null,
    int Balance = 0);

public enum ChallengeCreateError
{
    None,
    Usage,
    InvalidGame,
    InvalidAmount,
    TargetNotFound,
    SelfChallenge,
    NotEnoughCoins,
    AlreadyPending,
}

public sealed record ChallengeAcceptResult(
    ChallengeAcceptError Error,
    Challenge? Challenge = null,
    int ChallengerRoll = 0,
    int TargetRoll = 0,
    long WinnerId = 0,
    string WinnerName = "",
    int Payout = 0,
    int Fee = 0,
    bool IsTie = false);

public enum ChallengeAcceptError
{
    None,
    NotFound,
    NotTarget,
    Expired,
    AlreadyResolved,
    ChallengerNotEnoughCoins,
    TargetNotEnoughCoins,
    RollFailed,
}

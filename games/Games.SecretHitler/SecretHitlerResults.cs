using Games.SecretHitler.Domain;

namespace Games.SecretHitler;

public enum ShError
{
    None = 0,
    NotEnoughCoins,
    AlreadyInGame,
    GameNotFound,
    GameFull,
    GameInProgress,
    NotHost,
    NotInGame,
    NotEnoughPlayers,
    WrongPhase,
    NotPresident,
    NotChancellor,
    InvalidTarget,
    InvalidPolicy,
    TermLimited,
    AlreadyVoted,
}

public sealed record ShGameSnapshot(SecretHitlerGame Game, List<SecretHitlerPlayer> Players);

public sealed record ShCreateResult(ShError Error, string InviteCode, int BuyIn);
public sealed record ShJoinResult(ShError Error, ShGameSnapshot? Snapshot, int Joined, int Max);
public sealed record ShLeaveResult(ShError Error, ShGameSnapshot? Snapshot, bool GameClosed);
public sealed record ShStartResult(ShError Error, ShGameSnapshot? Snapshot);
public sealed record ShNominateResult(ShError Error, ShGameSnapshot? Snapshot);
public sealed record ShVoteResult(ShError Error, ShGameSnapshot? Snapshot, ShAfterVoteResult? After);
public sealed record ShDiscardResult(ShError Error, ShGameSnapshot? Snapshot);
public sealed record ShEnactResult(ShError Error, ShGameSnapshot? Snapshot, ShAfterEnactResult? After);

public static class ShResultHelpers
{
    public static ShCreateResult CreateFail(ShError e) => new(e, "", 0);
    public static ShJoinResult JoinFail(ShError e) => new(e, null, 0, 0);
    public static ShLeaveResult LeaveFail(ShError e) => new(e, null, false);
    public static ShStartResult StartFail(ShError e) => new(e, null);
    public static ShNominateResult NominateFail(ShError e) => new(e, null);
    public static ShVoteResult VoteFail(ShError e) => new(e, null, null);
    public static ShDiscardResult DiscardFail(ShError e) => new(e, null);
    public static ShEnactResult EnactFail(ShError e) => new(e, null, null);

    public static ShError MapValidation(ShValidation v) => v switch
    {
        ShValidation.NotPresident => ShError.NotPresident,
        ShValidation.NotChancellor => ShError.NotChancellor,
        ShValidation.NotYourTurn => ShError.WrongPhase,
        ShValidation.InvalidTarget => ShError.InvalidTarget,
        ShValidation.TermLimited => ShError.TermLimited,
        ShValidation.AlreadyVoted => ShError.AlreadyVoted,
        ShValidation.WrongPhase => ShError.WrongPhase,
        ShValidation.InvalidPolicy => ShError.InvalidPolicy,
        _ => ShError.None,
    };
}

using Games.Poker.Domain;

namespace Games.Poker;

public enum PokerError
{
    None = 0,
    NotEnoughCoins,
    AlreadySeated,
    TableNotFound,
    TableFull,
    HandInProgress,
    NotHost,
    NeedTwo,
    NoTable,
    NotYourTurn,
    CannotCheck,
    RaiseTooSmall,
    RaiseTooLarge,
    InvalidAction,
}

public enum HandTransition
{
    None = 0,
    HandStarted,
    PhaseAdvanced,
    TurnAdvanced,
    HandEnded,
}

public sealed record TableSnapshot(PokerTable Table, List<PokerSeat> Seats);

public sealed record CreateResult(PokerError Error, string InviteCode, int BuyIn);
public sealed record JoinResult(PokerError Error, TableSnapshot? Snapshot, int Seated, int Max);
public sealed record LeaveResult(PokerError Error, TableSnapshot? Snapshot, bool TableClosed);
public sealed record StartResult(PokerError Error, TableSnapshot? Snapshot);

public sealed record ActionResult(
    PokerError Error,
    TableSnapshot? Snapshot,
    HandTransition Transition,
    List<ShowdownEntry>? Showdown,
    string? AutoActorName,
    AutoAction? AutoKind);

public static class PokerResultHelpers
{
    public static CreateResult Fail(PokerError e) => new(e, "", 0);
    public static JoinResult JoinFail(PokerError e) => new(e, null, 0, 0);
    public static LeaveResult LeaveFail(PokerError e) => new(e, null, false);
    public static StartResult StartFail(PokerError e) => new(e, null);
    public static ActionResult ActionFail(PokerError e) => new(e, null, HandTransition.None, null, null, null);
}

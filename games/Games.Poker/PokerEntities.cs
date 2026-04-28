// ─────────────────────────────────────────────────────────────────────────────
// PokerEntities — mutable domain state objects that double as Dapper rows.
// The domain (PokerDomain.Apply / ResolveAfterAction) mutates these in place,
// so fields are intentionally plain property setters rather than records.
// ─────────────────────────────────────────────────────────────────────────────

namespace Games.Poker;

public enum PokerTableStatus
{
    Seating = 0,
    HandActive = 1,
    HandComplete = 2,
    Closed = 3,
}

public enum PokerPhase
{
    None = 0,
    PreFlop = 1,
    Flop = 2,
    Turn = 3,
    River = 4,
    Showdown = 5,
}

public enum PokerSeatStatus
{
    Seated = 0,
    Folded = 1,
    AllIn = 2,
    SittingOut = 3,
}

public sealed class PokerTable
{
    public string InviteCode { get; set; } = "";
    public long ChatId { get; set; }
    public long HostUserId { get; set; }
    public PokerTableStatus Status { get; set; } = PokerTableStatus.Seating;
    public PokerPhase Phase { get; set; } = PokerPhase.None;
    public int SmallBlind { get; set; }
    public int BigBlind { get; set; }
    public int Pot { get; set; }
    public string CommunityCards { get; set; } = "";
    public string DeckState { get; set; } = "";
    public int ButtonSeat { get; set; }
    public int CurrentSeat { get; set; }
    public int CurrentBet { get; set; }
    public int MinRaise { get; set; }
    public int? StateMessageId { get; set; }
    public long LastActionAt { get; set; }
    public long CreatedAt { get; set; }
}

public sealed class PokerSeat
{
    public string InviteCode { get; set; } = "";
    public int Position { get; set; }
    public long UserId { get; set; }
    public string DisplayName { get; set; } = "";
    public int Stack { get; set; }
    public string HoleCards { get; set; } = "";
    public PokerSeatStatus Status { get; set; } = PokerSeatStatus.Seated;
    public int CurrentBet { get; set; }
    public int TotalCommitted { get; set; }
    public bool HasActedThisRound { get; set; }
    public long ChatId { get; set; }
    public int? StateMessageId { get; set; }
    public long JoinedAt { get; set; }
}

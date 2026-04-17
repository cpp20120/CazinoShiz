using System.ComponentModel.DataAnnotations;

namespace CasinoShiz.Data.Entities;

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

public class PokerTable
{
    [Key]
    [MaxLength(8)]
    public string InviteCode { get; set; } = "";

    public long HostUserId { get; set; }
    public PokerTableStatus Status { get; set; } = PokerTableStatus.Seating;
    public PokerPhase Phase { get; set; } = PokerPhase.None;

    public int SmallBlind { get; set; }
    public int BigBlind { get; set; }
    public int Pot { get; set; }

    [MaxLength(32)]
    public string CommunityCards { get; set; } = "";

    [MaxLength(256)]
    public string DeckState { get; set; } = "";

    public int ButtonSeat { get; set; }
    public int CurrentSeat { get; set; }
    public int CurrentBet { get; set; }
    public int MinRaise { get; set; }

    public long LastActionAt { get; set; }
    public long CreatedAt { get; set; }
}

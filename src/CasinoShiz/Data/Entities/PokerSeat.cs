using System.ComponentModel.DataAnnotations;

namespace CasinoShiz.Data.Entities;

public enum PokerSeatStatus
{
    Seated = 0,
    Folded = 1,
    AllIn = 2,
    SittingOut = 3,
}

public class PokerSeat
{
    [MaxLength(8)]
    public string InviteCode { get; set; } = "";
    public int Position { get; set; }

    public long UserId { get; set; }

    [MaxLength(64)]
    public string DisplayName { get; set; } = "";
    public int Stack { get; set; }

    [MaxLength(8)]
    public string HoleCards { get; set; } = "";
    public PokerSeatStatus Status { get; set; } = PokerSeatStatus.Seated;
    public int CurrentBet { get; set; }
    public bool HasActedThisRound { get; set; }

    public long ChatId { get; set; }
    public int? StateMessageId { get; set; }
    public long JoinedAt { get; set; }
}

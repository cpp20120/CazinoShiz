using System.ComponentModel.DataAnnotations;

namespace CasinoShiz.Data.Entities;

public enum ShStatus
{
    Lobby = 0,
    Active = 1,
    Completed = 2,
    Closed = 3,
}

public enum ShPhase
{
    None = 0,
    Nomination = 1,
    Election = 2,
    LegislativePresident = 3,
    LegislativeChancellor = 4,
    GameEnd = 5,
}

public enum ShWinner
{
    None = 0,
    Liberals = 1,
    Fascists = 2,
}

public enum ShWinReason
{
    None = 0,
    LiberalPolicies = 1,
    FascistPolicies = 2,
    HitlerElected = 3,
    HitlerExecuted = 4,
}

public class SecretHitlerGame
{
    [Key]
    [MaxLength(8)]
    public string InviteCode { get; set; } = "";

    public long HostUserId { get; set; }
    public long ChatId { get; set; }
    public ShStatus Status { get; set; } = ShStatus.Lobby;
    public ShPhase Phase { get; set; } = ShPhase.None;

    public int LiberalPolicies { get; set; }
    public int FascistPolicies { get; set; }
    public int ElectionTracker { get; set; }

    public int CurrentPresidentPosition { get; set; }
    public int NominatedChancellorPosition { get; set; } = -1;
    public int LastElectedPresidentPosition { get; set; } = -1;
    public int LastElectedChancellorPosition { get; set; } = -1;

    [MaxLength(64)]
    public string DeckState { get; set; } = "";

    [MaxLength(64)]
    public string DiscardState { get; set; } = "";

    [MaxLength(8)]
    public string PresidentDraw { get; set; } = "";

    [MaxLength(8)]
    public string ChancellorReceived { get; set; } = "";

    public ShWinner Winner { get; set; } = ShWinner.None;
    public ShWinReason WinReason { get; set; } = ShWinReason.None;

    public int BuyIn { get; set; }
    public int Pot { get; set; }

    public long CreatedAt { get; set; }
    public long LastActionAt { get; set; }
}

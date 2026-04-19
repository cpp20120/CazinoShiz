namespace Games.SecretHitler;

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

public enum ShRole
{
    Liberal = 0,
    Fascist = 1,
    Hitler = 2,
}

public enum ShVote
{
    None = 0,
    Ja = 1,
    Nein = 2,
}

public sealed class SecretHitlerGame
{
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

    public string DeckState { get; set; } = "";
    public string DiscardState { get; set; } = "";
    public string PresidentDraw { get; set; } = "";
    public string ChancellorReceived { get; set; } = "";

    public ShWinner Winner { get; set; } = ShWinner.None;
    public ShWinReason WinReason { get; set; } = ShWinReason.None;

    public int BuyIn { get; set; }
    public int Pot { get; set; }

    public long CreatedAt { get; set; }
    public long LastActionAt { get; set; }
}

public sealed class SecretHitlerPlayer
{
    public string InviteCode { get; set; } = "";
    public int Position { get; set; }
    public long UserId { get; set; }
    public string DisplayName { get; set; } = "";
    public long ChatId { get; set; }
    public ShRole Role { get; set; } = ShRole.Liberal;
    public bool IsAlive { get; set; } = true;
    public ShVote LastVote { get; set; } = ShVote.None;
    public int? StateMessageId { get; set; }
    public long JoinedAt { get; set; }
}

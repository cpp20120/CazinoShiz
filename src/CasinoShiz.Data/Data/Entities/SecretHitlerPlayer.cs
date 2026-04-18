using System.ComponentModel.DataAnnotations;

namespace CasinoShiz.Data.Entities;

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

public class SecretHitlerPlayer
{
    [MaxLength(8)]
    public string InviteCode { get; set; } = "";
    public int Position { get; set; }

    public long UserId { get; set; }

    [MaxLength(64)]
    public string DisplayName { get; set; } = "";

    public long ChatId { get; set; }

    public ShRole Role { get; set; } = ShRole.Liberal;
    public bool IsAlive { get; set; } = true;
    public ShVote LastVote { get; set; } = ShVote.None;

    public int? StateMessageId { get; set; }
    public long JoinedAt { get; set; }
}

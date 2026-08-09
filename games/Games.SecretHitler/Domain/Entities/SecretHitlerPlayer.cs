namespace Games.SecretHitler.Domain.Entities;

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
    public string? WagerBetId { get; set; }
}

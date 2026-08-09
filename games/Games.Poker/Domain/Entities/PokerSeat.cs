// ─────────────────────────────────────────────────────────────────────────────
// PokerEntities — mutable domain state objects that double as Dapper rows.
// The domain (PokerDomain.Apply / ResolveAfterAction) mutates these in place,
// so fields are intentionally plain property setters rather than records.
// ─────────────────────────────────────────────────────────────────────────────

namespace Games.Poker.Domain.Entities;

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

    /// <summary>Wagering BetId; null for the legacy atomic wallet path.</summary>
    public string? WagerBetId { get; set; }
}

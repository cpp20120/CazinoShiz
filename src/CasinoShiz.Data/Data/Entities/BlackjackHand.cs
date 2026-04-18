using System.ComponentModel.DataAnnotations;

namespace CasinoShiz.Data.Entities;

public class BlackjackHand
{
    [Key]
    public long UserId { get; set; }

    public int Bet { get; set; }

    [MaxLength(64)]
    public string PlayerCards { get; set; } = "";

    [MaxLength(64)]
    public string DealerCards { get; set; } = "";

    [MaxLength(256)]
    public string DeckState { get; set; } = "";

    public long ChatId { get; set; }
    public int? StateMessageId { get; set; }
    public long CreatedAt { get; set; }
}

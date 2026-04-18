namespace CasinoShiz.Data.Entities;

public class DartsBet
{
    public long UserId { get; set; }
    public long ChatId { get; set; }
    public int Amount { get; set; }
    public long CreatedAt { get; set; }
}

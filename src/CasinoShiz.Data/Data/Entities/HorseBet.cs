using System.ComponentModel.DataAnnotations;

namespace CasinoShiz.Data.Entities;

public class HorseBet
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(10)]
    public string RaceDate { get; set; } = ""; // MM-dd-yyyy
    public int HorseId { get; set; } // 0-3
    public int Amount { get; set; }
    public long UserId { get; set; }
}

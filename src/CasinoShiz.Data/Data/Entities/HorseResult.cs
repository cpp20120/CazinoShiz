using System.ComponentModel.DataAnnotations;

namespace CasinoShiz.Data.Entities;

public class HorseResult
{
    [Key]
    [MaxLength(10)]
    public string RaceDate { get; set; } = ""; // MM-dd-yyyy

    public int Winner { get; set; }
    public byte[] ImageData { get; set; } = [];
}

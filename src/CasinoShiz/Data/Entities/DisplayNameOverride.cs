using System.ComponentModel.DataAnnotations;

namespace CasinoShiz.Data.Entities;

public class DisplayNameOverride
{
    [Key]
    [MaxLength(64)]
    public string OriginalName { get; set; } = "";

    [MaxLength(64)]
    public string NewName { get; set; } = "";
}

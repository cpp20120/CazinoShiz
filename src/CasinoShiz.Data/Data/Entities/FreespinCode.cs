using System.ComponentModel.DataAnnotations;

namespace CasinoShiz.Data.Entities;

public class FreespinCode
{
    [Key]
    public Guid Code { get; set; } = Guid.NewGuid();

    public bool Active { get; set; } = true;
    public long IssuedBy { get; set; }
    public long IssuedAt { get; set; }
    public long? ChatId { get; set; }
    public int? MessageId { get; set; }
}

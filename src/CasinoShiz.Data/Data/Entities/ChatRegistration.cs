using System.ComponentModel.DataAnnotations;

namespace CasinoShiz.Data.Entities;

public class ChatRegistration
{
    [Key]
    public long ChatId { get; set; }

    [MaxLength(255)]
    public string Name { get; set; } = "";

    [MaxLength(32)]
    public string? Username { get; set; }

    public bool NotificationsEnabled { get; set; } = true;
}

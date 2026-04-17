using System.ComponentModel.DataAnnotations;

namespace CasinoShiz.Data.Entities;

public class UserState
{
    [Key]
    public long TelegramUserId { get; set; }

    [MaxLength(64)]
    public string DisplayName { get; set; } = "";
    public int Coins { get; set; } = 100;
    public long LastDayUtc { get; set; }
    public int AttemptCount { get; set; }
    public int ExtraAttempts { get; set; }
}

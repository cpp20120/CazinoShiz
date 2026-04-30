namespace Games.Challenges;

public sealed class ChallengeOptions
{
    public const string SectionName = "Games:challenges";

    public int MinBet { get; set; } = 1;
    public int MaxBet { get; set; } = 5000;
    public int HouseFeeBasisPoints { get; set; } = 200;
    public int PendingTtlMinutes { get; set; } = 10;

    public TimeSpan PendingTtl => TimeSpan.FromMinutes(Math.Clamp(PendingTtlMinutes, 1, 60));
}

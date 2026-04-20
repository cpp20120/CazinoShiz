namespace Games.Darts;

public sealed class DartsOptions
{
    public const string SectionName = "Games:darts";
    public int MaxBet { get; init; } = 10_000;
}

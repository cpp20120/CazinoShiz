namespace Games.Horse;

public sealed class HorseOptions
{
    public const string SectionName = "Games:horse";

    public int HorseCount { get; init; } = 4;
    public int MinBetsToRun { get; init; } = 4;
    public int AnnounceDelayMs { get; init; } = 20_000;
    public List<long> Admins { get; init; } = [];
}

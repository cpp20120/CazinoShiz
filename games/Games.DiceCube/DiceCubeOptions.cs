namespace Games.DiceCube;

public sealed class DiceCubeOptions
{
    public const string SectionName = "Games:dicecube";
    public int MaxBet { get; init; } = 10_000;
}

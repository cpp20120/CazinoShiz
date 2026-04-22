namespace Games.DiceCube;

public sealed class DiceCubeOptions
{
    public const string SectionName = "Games:dicecube";
    public int MaxBet { get; init; } = 10_000;

    public int Mult4 { get; init; } = 1;
    public int Mult5 { get; init; } = 2;
    public int Mult6 { get; init; } = 3;

    /// <summary>0 = disabled. Min seconds after the previous round ended in this chat before a new <c>/dice bet</c>.</summary>
    public int MinSecondsBetweenBets { get; init; } = 8;
}

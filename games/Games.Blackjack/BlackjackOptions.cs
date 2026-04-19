namespace Games.Blackjack;

public sealed class BlackjackOptions
{
    public const string SectionName = "Games:blackjack";

    public int MinBet { get; init; } = 1;
    public int MaxBet { get; init; } = 1000;
    public int HandTimeoutMs { get; init; } = 120_000;
}

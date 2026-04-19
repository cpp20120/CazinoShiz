namespace Games.Poker;

public sealed class PokerOptions
{
    public const string SectionName = "Games:poker";

    public int BuyIn { get; init; } = 100;
    public int SmallBlind { get; init; } = 1;
    public int BigBlind { get; init; } = 2;
    public int MaxPlayers { get; init; } = 8;
    public int TurnTimeoutMs { get; init; } = 60_000;
}

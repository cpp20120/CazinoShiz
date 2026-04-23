namespace Games.Basketball;

public sealed class BasketballOptions
{
    public const string SectionName = "Games:basketball";
    public int MaxBet { get; init; } = 10_000;

    /// <summary>Used when user sends <c>/basket bet</c> without an amount.</summary>
    public int DefaultBet { get; init; } = 10;
}

namespace Games.Basketball;

public sealed class BasketballOptions
{
    public const string SectionName = "Games:basketball";
    public int MaxBet { get; init; } = 10_000;

    /// <summary>Used when user sends <c>/basket bet</c> without an amount.</summary>
    public int DefaultBet { get; init; } = 10;

    /// <summary>Chance per resolved 🏀 throw to drop a redeem code for one extra 🏀 throw. 0.01 = 1%.</summary>
    public double RedeemDropChance { get; init; }
}

namespace Games.Bowling;

public sealed class BowlingOptions
{
    public const string SectionName = "Games:bowling";

    public int MaxBet { get; init; } = 10_000;

    /// <summary>Used when user sends <c>/bowling bet</c> without an amount.</summary>
    public int DefaultBet { get; init; } = 10;

    /// <summary>Chance per resolved 🎳 roll to drop a redeem code for one extra 🎳 roll. 0.01 = 1%.</summary>
    public double RedeemDropChance { get; init; }
}

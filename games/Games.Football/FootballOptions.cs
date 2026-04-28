namespace Games.Football;

public sealed class FootballOptions
{
    public const string SectionName = "Games:football";
    public int MaxBet { get; init; } = 10_000;

    /// <summary>Used when user sends <c>/football</c> or <c>/football bet</c> without an amount.</summary>
    public int DefaultBet { get; init; } = 10;

    /// <summary>Chance per resolved ⚽ throw to drop a redeem code for one extra ⚽ throw. 0.01 = 1%.</summary>
    public double RedeemDropChance { get; init; }
}

namespace Games.Dice;

public sealed class DiceOptions
{
    public const string SectionName = "Games:dice";
    /// <summary>Stake per 🎰 spin; gas is added on top. Default 9 + gas ≈4–5% house edge for uniform Telegram slot values.</summary>
    public int Cost { get; init; } = 9;
}

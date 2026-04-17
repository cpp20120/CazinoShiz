namespace CasinoShiz.Configuration;

public sealed class BotOptions
{
    public const string SectionName = "Bot";

    public required string Token { get; init; }
    public bool IsProduction { get; init; }
    public List<long> Admins { get; init; } = [];
    public string CurrentKey { get; init; } = "busino-dev";
    public int AttemptsLimit { get; init; } = 3;
    public int DiceCost { get; init; } = 7;
    public double FreecodeProbability { get; init; } = 0.15;
    public int CaptchaItems { get; init; } = 5;
    public int DaysOfInactivityToHideInTop { get; init; } = 3;
    public int WebhookPort { get; init; } = 3000;
    public string TrustedChannel { get; init; } = "@businonews";
    public string? AdminWebToken { get; init; }

    public int PokerBuyIn { get; init; } = 100;
    public int PokerSmallBlind { get; init; } = 5;
    public int PokerBigBlind { get; init; } = 10;
    public int PokerMaxPlayers { get; init; } = 6;
    public int PokerTurnTimeoutMs { get; init; } = 30_000;

    public const string CasinoDice = "🎰";
    public static readonly string[] Stickers = ["bar", "cherry", "lemon", "seven"];
    public static readonly int[] StakePrice = [1, 1, 2, 3];
}

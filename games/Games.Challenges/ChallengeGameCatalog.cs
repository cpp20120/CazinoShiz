namespace Games.Challenges;

public static class ChallengeGameCatalog
{
    public static bool TryParse(string value, out ChallengeGame game)
    {
        game = default;
        return Normalize(value) switch
        {
            "dice" or "die" or "dicecube" or "cube" or "кубик" or "🎲" => Set(ChallengeGame.DiceCube, out game),
            "darts" or "dart" or "дартс" or "🎯" => Set(ChallengeGame.Darts, out game),
            "bowling" or "bowl" or "боулинг" or "🎳" => Set(ChallengeGame.Bowling, out game),
            "basket" or "basketball" or "баскетбол" or "🏀" => Set(ChallengeGame.Basketball, out game),
            "football" or "soccer" or "футбол" or "⚽" => Set(ChallengeGame.Football, out game),
            "slots" or "slot" or "casino" or "слоты" or "🎰" => Set(ChallengeGame.Slots, out game),
            "horse" or "horses" or "race" or "лошади" or "скачки" or "🐎" => Set(ChallengeGame.Horse, out game),
            "blackjack" or "bj" or "21" or "блекджек" or "🃏" => Set(ChallengeGame.Blackjack, out game),
            _ => false,
        };
    }

    public static string Emoji(ChallengeGame game) => game switch
    {
        ChallengeGame.Dice => "🎲",
        ChallengeGame.DiceCube => "🎲",
        ChallengeGame.Darts => "🎯",
        ChallengeGame.Bowling => "🎳",
        ChallengeGame.Basketball => "🏀",
        ChallengeGame.Football => "⚽",
        ChallengeGame.Slots => "🎰",
        ChallengeGame.Horse => "🐎",
        ChallengeGame.Blackjack => "🃏",
        _ => "🎲",
    };

    public static string DisplayName(ChallengeGame game) => game switch
    {
        ChallengeGame.Dice => "dice",
        ChallengeGame.DiceCube => "dicecube",
        ChallengeGame.Darts => "darts",
        ChallengeGame.Bowling => "bowling",
        ChallengeGame.Basketball => "basketball",
        ChallengeGame.Football => "football",
        ChallengeGame.Slots => "slots",
        ChallengeGame.Horse => "horse",
        ChallengeGame.Blackjack => "blackjack",
        _ => game.ToString().ToLowerInvariant(),
    };

    private static string Normalize(string value) => value.Trim().ToLowerInvariant();

    private static bool Set(ChallengeGame source, out ChallengeGame target)
    {
        target = source;
        return true;
    }
}

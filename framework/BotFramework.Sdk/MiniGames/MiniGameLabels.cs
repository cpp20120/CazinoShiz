namespace BotFramework.Sdk;

/// <summary>Short RU labels for cross-game messages (matches module copy tone).</summary>
public static class MiniGameLabels
{
    public static string Ru(string gameId) => gameId switch
    {
        MiniGameIds.DiceCube => "куб 🎲",
        MiniGameIds.Darts => "дартс 🎯",
        MiniGameIds.Football => "футбол ⚽",
        MiniGameIds.Basketball => "баскет 🏀",
        MiniGameIds.Bowling => "боулинг 🎳",
        _ => gameId,
    };
}

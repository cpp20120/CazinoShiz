using CasinoShiz.Helpers;

namespace CasinoShiz.Services.Poker.Domain;

public static class Deck
{
    private static readonly string[] Suits = ["S", "H", "D", "C"];
    private static readonly string[] Ranks = ["2", "3", "4", "5", "6", "7", "8", "9", "T", "J", "Q", "K", "A"];

    public static string BuildShuffled(int seed)
    {
        var deck = new List<string>(52);
        deck.AddRange(from s in Suits from r in Ranks select r + s);

        var rng = new Mulberry32(seed);
        for (var i = deck.Count - 1; i > 0; i--)
        {
            var j = (int)(rng.Next() * (i + 1));
            if (j > i) j = i;
            (deck[i], deck[j]) = (deck[j], deck[i]);
        }
        return string.Join(" ", deck);
    }

    public static string[] Draw(ref string deck, int count)
    {
        var cards = deck.Length == 0
            ? Array.Empty<string>()
            : deck.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (cards.Length < count) throw new InvalidOperationException("Deck exhausted");

        var drawn = new string[count];
        Array.Copy(cards, 0, drawn, 0, count);
        deck = string.Join(" ", cards.Skip(count));
        return drawn;
    }

    public static string[] Parse(string cards)
    {
        return string.IsNullOrWhiteSpace(cards) ? Array.Empty<string>() : cards.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }
}

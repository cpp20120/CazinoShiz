using System.Security.Cryptography;

namespace Games.SecretHitler.Domain;

public enum ShPolicy { Liberal, Fascist }

public static class ShPolicyDeck
{
    private const int TotalLiberal = 6;
    private const int TotalFascist = 11;
    public const int MinDrawPoolSize = 3;

    public static string BuildShuffledDeck()
    {
        var deck = new List<ShPolicy>(TotalLiberal + TotalFascist);
        for (var i = 0; i < TotalLiberal; i++) deck.Add(ShPolicy.Liberal);
        for (var i = 0; i < TotalFascist; i++) deck.Add(ShPolicy.Fascist);

        for (var i = deck.Count - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (deck[i], deck[j]) = (deck[j], deck[i]);
        }

        return Serialize(deck);
    }

    public static string Serialize(IEnumerable<ShPolicy> policies) =>
        string.Join("", policies.Select(p => p == ShPolicy.Liberal ? 'L' : 'F'));

    public static List<ShPolicy> Parse(string state) =>
        state.Select(c => c == 'L' ? ShPolicy.Liberal : ShPolicy.Fascist).ToList();

    public static ShPolicy[] Draw(ref string deckState, ref string discardState, int count)
    {
        var deck = Parse(deckState);
        if (deck.Count < count)
        {
            var discard = Parse(discardState);
            deck.AddRange(discard);
            for (int i = deck.Count - 1; i > 0; i--)
            {
                int j = RandomNumberGenerator.GetInt32(i + 1);
                (deck[i], deck[j]) = (deck[j], deck[i]);
            }
            discardState = "";
        }

        var drawn = deck.Take(count).ToArray();
        deck.RemoveRange(0, count);
        deckState = Serialize(deck);
        return drawn;
    }

    public static ShPolicy PeekTop(string deckState, string discardState)
    {
        var deck = Parse(deckState);
        if (deck.Count == 0) deck.AddRange(Parse(discardState));
        return deck[0];
    }

    public static void AddToDiscard(ref string discardState, ShPolicy policy)
    {
        discardState += policy == ShPolicy.Liberal ? 'L' : 'F';
    }
}

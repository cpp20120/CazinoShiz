namespace Games.Blackjack.Domain;

public static class BlackjackHandValue
{
    public static int CardValue(string card)
    {
        var rank = card[..^1];
        return rank switch
        {
            "A" => 11,
            "T" or "J" or "Q" or "K" => 10,
            _ => int.Parse(rank),
        };
    }

    public static int Compute(IReadOnlyList<string> cards)
    {
        var total = 0;
        var aces = 0;
        foreach (var c in cards)
        {
            var v = CardValue(c);
            total += v;
            if (v == 11) aces++;
        }
        while (total > 21 && aces > 0)
        {
            total -= 10;
            aces--;
        }
        return total;
    }

    public static bool IsNaturalBlackjack(IReadOnlyList<string> cards) =>
        cards.Count == 2 && Compute(cards) == 21;
}

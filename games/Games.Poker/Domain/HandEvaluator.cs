namespace Games.Poker.Domain;

public enum HandCategory
{
    HighCard = 0,
    Pair = 1,
    TwoPair = 2,
    ThreeOfAKind = 3,
    Straight = 4,
    Flush = 5,
    FullHouse = 6,
    FourOfAKind = 7,
    StraightFlush = 8,
}

public readonly struct HandRank(HandCategory category, int[] tiebreakers) : IComparable<HandRank>
{
    public HandCategory Category { get; } = category;
    private int[] Tiebreakers { get; } = tiebreakers;

    public int CompareTo(HandRank other)
    {
        var c = ((int)Category).CompareTo((int)other.Category);
        if (c != 0) return c;
        var len = Math.Min(Tiebreakers.Length, other.Tiebreakers.Length);
        for (var i = 0; i < len; i++)
        {
            c = Tiebreakers[i].CompareTo(other.Tiebreakers[i]);
            if (c != 0) return c;
        }
        return 0;
    }

    public override string ToString() => $"{Category}[{string.Join(",", Tiebreakers)}]";
}

public static class HandEvaluator
{
    private static int RankOf(string card) => card[0] switch
    {
        '2' => 2, '3' => 3, '4' => 4, '5' => 5, '6' => 6,
        '7' => 7, '8' => 8, '9' => 9, 'T' => 10,
        'J' => 11, 'Q' => 12, 'K' => 13, 'A' => 14,
        _ => throw new ArgumentException($"Invalid card: {card}"),
    };

    private static char SuitOf(string card) => card[1];

    public static HandRank EvaluateBest(IEnumerable<string> sevenCards)
    {
        var cards = sevenCards.Where(c => !string.IsNullOrEmpty(c)).ToArray();
        if (cards.Length < 5) throw new ArgumentException("Need at least 5 cards");

        HandRank best = default;
        var hasBest = false;
        var n = cards.Length;
        for (var a = 0; a < n - 4; a++)
        for (var b = a + 1; b < n - 3; b++)
        for (var c = b + 1; c < n - 2; c++)
        for (var d = c + 1; d < n - 1; d++)
        for (var e = d + 1; e < n; e++)
        {
            var five = new[] { cards[a], cards[b], cards[c], cards[d], cards[e] };
            var rank = EvaluateFive(five);
            if (hasBest && rank.CompareTo(best) <= 0) continue;
            best = rank;
            hasBest = true;
        }
        return best;
    }

    private static HandRank EvaluateFive(string[] five)
    {
        var ranks = five.Select(RankOf).OrderByDescending(r => r).ToArray();
        var suits = five.Select(SuitOf).ToArray();
        var isFlush = suits.Distinct().Count() == 1;

        var groups = ranks
            .GroupBy(r => r)
            .Select(g => new { Rank = g.Key, Count = g.Count() })
            .OrderByDescending(g => g.Count)
            .ThenByDescending(g => g.Rank)
            .ToArray();

        var straightHigh = StraightHigh(ranks);
        var isStraight = straightHigh > 0;

        if (isStraight && isFlush)
            return new HandRank(HandCategory.StraightFlush, new[] { straightHigh });

        switch (groups[0].Count)
        {
            case 4:
            {
                var kicker = ranks.First(r => r != groups[0].Rank);
                return new HandRank(HandCategory.FourOfAKind, new[] { groups[0].Rank, kicker });
            }
            case 3 when groups.Length > 1 && groups[1].Count == 2:
                return new HandRank(HandCategory.FullHouse, new[] { groups[0].Rank, groups[1].Rank });
        }

        if (isFlush)
            return new HandRank(HandCategory.Flush, ranks);

        if (isStraight)
            return new HandRank(HandCategory.Straight, new[] { straightHigh });

        switch (groups[0].Count)
        {
            case 3:
            {
                var kickers = ranks.Where(r => r != groups[0].Rank).Take(2).ToArray();
                return new HandRank(HandCategory.ThreeOfAKind, new[] { groups[0].Rank, kickers[0], kickers[1] });
            }
            case 2 when groups.Length > 1 && groups[1].Count == 2:
            {
                var high = Math.Max(groups[0].Rank, groups[1].Rank);
                var low = Math.Min(groups[0].Rank, groups[1].Rank);
                var kicker = ranks.First(r => r != high && r != low);
                return new HandRank(HandCategory.TwoPair, new[] { high, low, kicker });
            }
        }

        if (groups[0].Count != 2) return new HandRank(HandCategory.HighCard, ranks);
        {
            var kickers = ranks.Where(r => r != groups[0].Rank).Take(3).ToArray();
            return new HandRank(HandCategory.Pair, [groups[0].Rank, kickers[0], kickers[1], kickers[2]]);
        }
    }

    private static int StraightHigh(int[] sortedDesc)
    {
        var unique = sortedDesc.Distinct().OrderByDescending(r => r).ToArray();
        if (unique.Length < 5) return 0;

        for (var i = 0; i <= unique.Length - 5; i++)
        {
            if (unique[i] - unique[i + 4] == 4)
                return unique[i];
        }

        if (unique.Contains(14) && unique.Contains(2) && unique.Contains(3)
            && unique.Contains(4) && unique.Contains(5))
            return 5;

        return 0;
    }

    public static string CategoryNameRu(HandCategory c) => c switch
    {
        HandCategory.StraightFlush => "Стрит-флеш",
        HandCategory.FourOfAKind => "Каре",
        HandCategory.FullHouse => "Фулл-хаус",
        HandCategory.Flush => "Флеш",
        HandCategory.Straight => "Стрит",
        HandCategory.ThreeOfAKind => "Тройка",
        HandCategory.TwoPair => "Две пары",
        HandCategory.Pair => "Пара",
        HandCategory.HighCard => "Старшая карта",
        _ => "?",
    };
}

using Games.Poker.Domain;
using Xunit;

namespace CasinoShiz.Tests;

public class PokerDeckTests
{
    [Fact]
    public void BuildShuffled_Returns52Cards()
    {
        var deck = Deck.BuildShuffled();
        var cards = deck.Split(' ');
        Assert.Equal(52, cards.Length);
    }

    [Fact]
    public void BuildShuffled_AllCardsUnique()
    {
        var deck = Deck.BuildShuffled();
        var cards = deck.Split(' ');
        Assert.Equal(52, cards.Distinct().Count());
    }

    [Fact]
    public void BuildShuffled_ContainsAllSuitsAndRanks()
    {
        var deck = Deck.BuildShuffled();
        var cards = deck.Split(' ');
        var suits = new[] { "S", "H", "D", "C" };
        var ranks = new[] { "2", "3", "4", "5", "6", "7", "8", "9", "T", "J", "Q", "K", "A" };
        foreach (var s in suits)
            foreach (var r in ranks)
                Assert.Contains(r + s, cards);
    }

    [Fact]
    public void Draw_RemovesCardsFromDeck()
    {
        var deck = Deck.BuildShuffled();
        var before = deck.Split(' ').Length;
        Deck.Draw(ref deck, 2);
        var after = deck.Split(' ').Length;
        Assert.Equal(before - 2, after);
    }

    [Fact]
    public void Draw_ReturnsRequestedCount()
    {
        var deck = Deck.BuildShuffled();
        var drawn = Deck.Draw(ref deck, 7);
        Assert.Equal(7, drawn.Length);
    }

    [Fact]
    public void Draw_ThrowsWhenDeckExhausted()
    {
        var deck = "AS KH";
        Assert.Throws<InvalidOperationException>(() => Deck.Draw(ref deck, 5));
    }

    [Theory]
    [InlineData("", 0)]
    [InlineData("AS KH QD", 3)]
    [InlineData("2S 3H 4D 5C 6S 7H 8D", 7)]
    public void Parse_ReturnsSplitCards(string input, int expectedCount)
    {
        var result = Deck.Parse(input);
        Assert.Equal(expectedCount, result.Length);
    }

    [Fact]
    public void Draw_MultipleConsecutive_NoOverlap()
    {
        var deck = Deck.BuildShuffled();
        var hand1 = Deck.Draw(ref deck, 2);
        var hand2 = Deck.Draw(ref deck, 2);
        Assert.Empty(hand1.Intersect(hand2));
    }
}

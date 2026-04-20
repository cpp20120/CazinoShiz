using Games.Blackjack.Domain;
using Xunit;

namespace CasinoShiz.Tests;

public class BlackjackDeckTests
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
        Deck.Draw(ref deck, 3);
        var after = deck.Split(' ').Length;
        Assert.Equal(before - 3, after);
    }

    [Fact]
    public void Draw_ReturnsRequestedCount()
    {
        var deck = Deck.BuildShuffled();
        var drawn = Deck.Draw(ref deck, 5);
        Assert.Equal(5, drawn.Length);
    }

    [Fact]
    public void Draw_ThrowsWhenDeckExhausted()
    {
        var deck = "AS";
        Assert.Throws<InvalidOperationException>(() => Deck.Draw(ref deck, 2));
    }

    [Theory]
    [InlineData("", 0)]
    [InlineData("AS KH", 2)]
    [InlineData("2S 3H 4D 5C", 4)]
    public void Parse_ReturnsSplitCards(string input, int expectedCount)
    {
        var result = Deck.Parse(input);
        Assert.Equal(expectedCount, result.Length);
    }
}

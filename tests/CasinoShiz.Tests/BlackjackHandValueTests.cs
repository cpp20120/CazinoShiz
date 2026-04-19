using Games.Blackjack.Domain;
using Xunit;

namespace CasinoShiz.Tests;

public class BlackjackHandValueTests
{
    [Theory]
    [InlineData(new[] { "AS", "KH" }, 21)]
    [InlineData(new[] { "AS", "9D" }, 20)]
    [InlineData(new[] { "AS", "AD" }, 12)]
    [InlineData(new[] { "AS", "AD", "9H" }, 21)]
    [InlineData(new[] { "AS", "KD", "5H" }, 16)]
    [InlineData(new[] { "TC", "QH" }, 20)]
    [InlineData(new[] { "5S", "5H", "5D" }, 15)]
    [InlineData(new[] { "KS", "KH", "KD" }, 30)]
    public void Compute_handlesAcesAndFaceCards(string[] cards, int expected)
    {
        Assert.Equal(expected, BlackjackHandValue.Compute(cards));
    }

    [Theory]
    [InlineData(new[] { "AS", "KH" }, true)]
    [InlineData(new[] { "AS", "TD" }, true)]
    [InlineData(new[] { "KS", "QH" }, false)]
    [InlineData(new[] { "AS", "5D", "5H" }, false)]
    public void IsNaturalBlackjack_requiresExactlyTwoCardsTotalingTwentyOne(string[] cards, bool expected)
    {
        Assert.Equal(expected, BlackjackHandValue.IsNaturalBlackjack(cards));
    }
}

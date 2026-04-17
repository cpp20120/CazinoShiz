using CasinoShiz.Services.Poker.Domain;
using Xunit;

namespace BusinoBot.Tests;

public class HandEvaluatorTests
{
    [Fact]
    public void RoyalFlush_BeatsFourOfAKind()
    {
        var royal = HandEvaluator.EvaluateBest(["AS", "KS", "QS", "JS", "TS", "2H", "3H"]);
        var quads = HandEvaluator.EvaluateBest(["AS", "AH", "AD", "AC", "KS", "QH", "JH"]);
        Assert.True(royal.CompareTo(quads) > 0);
    }

    [Fact]
    public void FullHouse_BeatsFlush()
    {
        var fullHouse = HandEvaluator.EvaluateBest(["KS", "KH", "KD", "2S", "2H", "3C", "4C"]);
        var flush = HandEvaluator.EvaluateBest(["AH", "KH", "QH", "JH", "9H", "2S", "3C"]);
        Assert.True(fullHouse.CompareTo(flush) > 0);
    }

    [Fact]
    public void Pair_BeatsHighCard()
    {
        var pair = HandEvaluator.EvaluateBest(["AS", "AH", "KD", "QS", "JH", "9C", "7D"]);
        var highCard = HandEvaluator.EvaluateBest(["AS", "KH", "QD", "JC", "9S", "7H", "5D"]);
        Assert.True(pair.CompareTo(highCard) > 0);
    }

    [Fact]
    public void Straight_WheelAceLow()
    {
        var wheel = HandEvaluator.EvaluateBest(["AS", "2H", "3D", "4C", "5S", "KH", "QD"]);
        Assert.Equal(HandCategory.Straight, wheel.Category);
    }

    [Fact]
    public void Category_MapsCorrectly()
    {
        Assert.Equal(HandCategory.StraightFlush,
            HandEvaluator.EvaluateBest(["9S", "8S", "7S", "6S", "5S", "2H", "3D"]).Category);
        Assert.Equal(HandCategory.FourOfAKind,
            HandEvaluator.EvaluateBest(["9S", "9H", "9D", "9C", "5S", "2H", "3D"]).Category);
        Assert.Equal(HandCategory.ThreeOfAKind,
            HandEvaluator.EvaluateBest(["9S", "9H", "9D", "5S", "3C", "2H", "8D"]).Category);
        Assert.Equal(HandCategory.TwoPair,
            HandEvaluator.EvaluateBest(["9S", "9H", "5D", "5S", "3C", "2H", "8D"]).Category);
    }
}

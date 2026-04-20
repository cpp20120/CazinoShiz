using Games.Poker.Domain;
using Xunit;

namespace CasinoShiz.Tests;

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

    // ── Wheel straight (A-2-3-4-5) ──────────────────────────────────────────

    [Fact]
    public void WheelStraight_BeatsFourOfAKindFalse()
    {
        var wheel = HandEvaluator.EvaluateBest(["AS", "2H", "3D", "4C", "5S", "KH", "QD"]);
        Assert.Equal(HandCategory.Straight, wheel.Category);
    }

    [Fact]
    public void WheelStraight_HighIs5()
    {
        // Wheel straight high card should be 5, not ace
        var wheel = HandEvaluator.EvaluateBest(["AS", "2H", "3D", "4C", "5S", "9H", "8D"]);
        var broadway = HandEvaluator.EvaluateBest(["AS", "KH", "QD", "JC", "TS", "2H", "3D"]);
        Assert.True(broadway.CompareTo(wheel) > 0, "Broadway (T-A) beats wheel (A-5)");
    }

    // ── Kicker tiebreakers ───────────────────────────────────────────────────

    [Fact]
    public void Pair_HigherKicker_Wins()
    {
        var aceKicker = HandEvaluator.EvaluateBest(["KS", "KH", "AS", "QD", "JC", "9S", "7H"]);
        var queenKicker = HandEvaluator.EvaluateBest(["KS", "KH", "QS", "JD", "9C", "8S", "6H"]);
        Assert.True(aceKicker.CompareTo(queenKicker) > 0);
    }

    [Fact]
    public void TwoPair_HigherTopPair_Wins()
    {
        var acesAndDeuces = HandEvaluator.EvaluateBest(["AS", "AH", "2S", "2H", "KC", "QD", "JC"]);
        var kingsAndDeuces = HandEvaluator.EvaluateBest(["KS", "KH", "2S", "2H", "QC", "JD", "TC"]);
        Assert.True(acesAndDeuces.CompareTo(kingsAndDeuces) > 0);
    }

    [Fact]
    public void TwoPair_SamePairs_KickerDecides()
    {
        var aceKicker = HandEvaluator.EvaluateBest(["KS", "KH", "QS", "QH", "AS", "2D", "3C"]);
        var jackKicker = HandEvaluator.EvaluateBest(["KS", "KH", "QS", "QH", "JS", "2D", "3C"]);
        Assert.True(aceKicker.CompareTo(jackKicker) > 0);
    }

    [Fact]
    public void FullHouse_HigherTrips_Wins()
    {
        var acesFullOfKings = HandEvaluator.EvaluateBest(["AS", "AH", "AD", "KS", "KH", "2C", "3D"]);
        var kingsFullOfAces = HandEvaluator.EvaluateBest(["KS", "KH", "KD", "AS", "AH", "2C", "3D"]);
        Assert.True(acesFullOfKings.CompareTo(kingsFullOfAces) > 0);
    }

    [Fact]
    public void FourOfAKind_HigherQuads_Wins()
    {
        var aceQuads = HandEvaluator.EvaluateBest(["AS", "AH", "AD", "AC", "KS", "QH", "JD"]);
        var kingQuads = HandEvaluator.EvaluateBest(["KS", "KH", "KD", "KC", "AS", "QH", "JD"]);
        Assert.True(aceQuads.CompareTo(kingQuads) > 0);
    }

    // ── CategoryNameRu ───────────────────────────────────────────────────────

    [Theory]
    [InlineData(HandCategory.StraightFlush, "Стрит-флеш")]
    [InlineData(HandCategory.FourOfAKind, "Каре")]
    [InlineData(HandCategory.FullHouse, "Фулл-хаус")]
    [InlineData(HandCategory.Flush, "Флеш")]
    [InlineData(HandCategory.Straight, "Стрит")]
    [InlineData(HandCategory.ThreeOfAKind, "Тройка")]
    [InlineData(HandCategory.TwoPair, "Две пары")]
    [InlineData(HandCategory.Pair, "Пара")]
    [InlineData(HandCategory.HighCard, "Старшая карта")]
    public void CategoryNameRu_ReturnsCorrectString(HandCategory cat, string expected)
    {
        Assert.Equal(expected, HandEvaluator.CategoryNameRu(cat));
    }

    // ── Best 5 from 7 selection ──────────────────────────────────────────────

    [Fact]
    public void EvaluateBest_SevenCards_PicksBestFive()
    {
        // Board: 2S 3H 4D 5C KH  — hole: AS 9D  → best is wheel (A2345 straight)
        // But also could pick 9-high. Ensure straight is detected.
        var rank = HandEvaluator.EvaluateBest(["AS", "9D", "2S", "3H", "4D", "5C", "KH"]);
        Assert.Equal(HandCategory.Straight, rank.Category);
    }

    [Fact]
    public void EvaluateBest_ExactlyFiveCards_Works()
    {
        var rank = HandEvaluator.EvaluateBest(["AS", "KS", "QS", "JS", "TS"]);
        Assert.Equal(HandCategory.StraightFlush, rank.Category);
    }

    [Fact]
    public void EvaluateBest_HighCard_SevenDistinctRanks()
    {
        var rank = HandEvaluator.EvaluateBest(["AS", "KH", "QD", "JC", "9S", "7H", "5D"]);
        Assert.Equal(HandCategory.HighCard, rank.Category);
    }
}

using BotFramework.Host.Services.Random;
using Xunit;

namespace CasinoShiz.Tests;

public class ArrayExtensionsTests
{
    // ── Shuffle ──────────────────────────────────────────────────────────────

    [Fact]
    public void Shuffle_ReturnsSameLengthArray()
    {
        var arr = new[] { 1, 2, 3, 4, 5 };
        var result = ArrayExtensions.Shuffle(arr, new Mulberry32(42).Next);
        Assert.Equal(arr.Length, result.Length);
    }

    [Fact]
    public void Shuffle_ContainsSameElements()
    {
        var arr = new[] { 1, 2, 3, 4, 5 };
        var result = ArrayExtensions.Shuffle(arr, new Mulberry32(42).Next);
        Assert.Equal(arr.OrderBy(x => x), result.OrderBy(x => x));
    }

    [Fact]
    public void Shuffle_DoesNotMutateOriginal()
    {
        var arr = new[] { 1, 2, 3, 4, 5 };
        var copy = arr.ToArray();
        ArrayExtensions.Shuffle(arr, new Mulberry32(42).Next);
        Assert.Equal(copy, arr);
    }

    [Fact]
    public void Shuffle_DeterministicWithSameSeed()
    {
        var arr = new[] { 'a', 'b', 'c', 'd', 'e' };
        var r1 = ArrayExtensions.Shuffle(arr, new Mulberry32(99).Next);
        var r2 = ArrayExtensions.Shuffle(arr, new Mulberry32(99).Next);
        Assert.Equal(r1, r2);
    }

    [Fact]
    public void Shuffle_EmptyArray_ReturnsEmpty()
    {
        var result = ArrayExtensions.Shuffle(Array.Empty<int>(), new Mulberry32(1).Next);
        Assert.Empty(result);
    }

    [Fact]
    public void Shuffle_SingleElement_ReturnsSameElement()
    {
        var result = ArrayExtensions.Shuffle(new[] { 42 }, new Mulberry32(1).Next);
        Assert.Equal(42, result[0]);
    }

    // ── GetRandom ────────────────────────────────────────────────────────────

    [Fact]
    public void GetRandom_AlwaysReturnsElementFromArray()
    {
        var arr = new[] { 10, 20, 30, 40, 50 };
        for (var i = 0; i < 50; i++)
        {
            var result = ArrayExtensions.GetRandom(arr, new Mulberry32(i).Next);
            Assert.Contains(result, arr);
        }
    }

    [Fact]
    public void GetRandom_Deterministic_WithSameSeed()
    {
        var arr = new[] { "a", "b", "c", "d" };
        var r1 = ArrayExtensions.GetRandom(arr, new Mulberry32(7).Next);
        var r2 = ArrayExtensions.GetRandom(arr, new Mulberry32(7).Next);
        Assert.Equal(r1, r2);
    }

    [Fact]
    public void GetRandom_SingleElement_ReturnsThatElement()
    {
        var arr = new[] { "only" };
        var result = ArrayExtensions.GetRandom(arr, new Mulberry32(1).Next);
        Assert.Equal("only", result);
    }

    [Fact]
    public void GetRandom_NullGetRandom_UsesSharedRandom()
    {
        var arr = new[] { 1, 2, 3 };
        var ex = Record.Exception(() => ArrayExtensions.GetRandom(arr));
        Assert.Null(ex);
    }

    // ── GetSomeRandom ────────────────────────────────────────────────────────

    [Fact]
    public void GetSomeRandom_ReturnsRequestedCount()
    {
        var arr = Enumerable.Range(0, 20).ToArray();
        var result = ArrayExtensions.GetSomeRandom(arr, 6, new Mulberry32(42).Next);
        Assert.Equal(6, result.Length);
    }

    [Fact]
    public void GetSomeRandom_AllResultsFromSource()
    {
        var arr = Enumerable.Range(0, 20).ToArray();
        var result = ArrayExtensions.GetSomeRandom(arr, 5, new Mulberry32(42).Next);
        Assert.All(result, item => Assert.Contains(item, arr));
    }

    [Fact]
    public void GetSomeRandom_Deterministic_WithSameSeed()
    {
        var arr = Enumerable.Range(0, 20).ToArray();
        var r1 = ArrayExtensions.GetSomeRandom(arr, 6, new Mulberry32(123).Next);
        var r2 = ArrayExtensions.GetSomeRandom(arr, 6, new Mulberry32(123).Next);
        Assert.Equal(r1, r2);
    }
}


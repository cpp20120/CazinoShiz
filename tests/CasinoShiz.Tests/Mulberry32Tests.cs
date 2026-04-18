using CasinoShiz.Helpers;
using Xunit;

namespace CasinoShiz.Tests;

public class Mulberry32Tests
{
    [Fact]
    public void Deterministic_SameSeed_SameSequence()
    {
        var a = new Mulberry32(42);
        var b = new Mulberry32(42);
        for (var i = 0; i < 10; i++)
            Assert.Equal(a.Next(), b.Next());
    }

    [Fact]
    public void DifferentSeeds_DifferentSequences()
    {
        var a = new Mulberry32(1);
        var b = new Mulberry32(2);
        Assert.NotEqual(a.Next(), b.Next());
    }

    [Fact]
    public void Next_WithinUnitInterval()
    {
        var rng = new Mulberry32(123);
        for (var i = 0; i < 100; i++)
        {
            var v = rng.Next();
            Assert.InRange(v, 0.0, 1.0);
        }
    }
}

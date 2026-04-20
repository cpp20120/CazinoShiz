using BotFramework.Host.Services.Random;
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

    // ── UuidToSeed ───────────────────────────────────────────────────────────

    [Fact]
    public void UuidToSeed_SameUuid_ReturnsSameSeed()
    {
        var uuid = "550e8400-e29b-41d4-a716-446655440000";
        Assert.Equal(Mulberry32.UuidToSeed(uuid), Mulberry32.UuidToSeed(uuid));
    }

    [Fact]
    public void UuidToSeed_DifferentUuids_DifferentSeeds()
    {
        var s1 = Mulberry32.UuidToSeed("550e8400-e29b-41d4-a716-446655440000");
        var s2 = Mulberry32.UuidToSeed("660e8400-e29b-41d4-a716-446655440000");
        Assert.NotEqual(s1, s2);
    }

    [Fact]
    public void UuidToSeed_ReturnsNonNegative()
    {
        var seed = Mulberry32.UuidToSeed(Guid.NewGuid().ToString());
        Assert.True(seed >= 0);
    }
}

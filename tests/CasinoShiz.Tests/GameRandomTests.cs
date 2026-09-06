using BotFramework.Sdk.Execution;
using Xunit;

namespace CasinoShiz.Tests;

public sealed class GameRandomTests
{
    [Fact]
    public void EntropyRandom_MapsNamedValuesToOutcomesAndCapturesTrace()
    {
        var random = new EntropyGameRandom(new EntropyValue(
        [
            KeyValuePair.Create("roll", 0.25),
            KeyValuePair.Create("pick", 0.99),
            KeyValuePair.Create("deck:0", 0.4),
            KeyValuePair.Create("deck:1", 0.1),
        ]));

        var roll = random.NextInt("roll", 6);
        var picked = random.Pick("pick", new[] { "a", "b", "c" });
        var deck = random.Shuffle("deck", new[] { "a", "b", "c" });

        Assert.Equal(1, roll);
        Assert.Equal("c", picked);
        Assert.Equal(["c", "a", "b"], deck);
        Assert.Equal(["roll", "pick", "deck:0", "deck:1"], random.Trace.Draws.Select(draw => draw.Name));
        Assert.Throws<InvalidOperationException>(() => random.NextUnit("roll"));
    }

    [Fact]
    public void EntropyRandom_UsesOnlyExecutorSuppliedEntropyAndStableNameBuilders()
    {
        var random = new EntropyGameRandom(new EntropyValue(
        [KeyValuePair.Create("chance", 0.49)]));

        Assert.True(random.Chance("chance", 0.5));
        Assert.Equal(["deck:0", "deck:1", "deck:2"], EntropyNames.ForShuffle("deck", 4));
        Assert.Equal(["draw:0", "draw:1"], EntropyNames.ForDraws("draw", 2));
        Assert.Throws<KeyNotFoundException>(() => random.NextUnit("undeclared"));
    }
}

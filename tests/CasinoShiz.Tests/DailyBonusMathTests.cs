using BotFramework.Host.Services;
using Xunit;

namespace CasinoShiz.Tests;

public class DailyBonusMathTests
{
    [Theory]
    [InlineData(10_000, 0.35, 8, 8)]
    [InlineData(1_000, 0.35, 8, 3)]
    [InlineData(285, 0.35, 8, 0)]
    [InlineData(100, 0.35, 8, 0)]
    [InlineData(1_000, 0.0, 8, 0)]
    public void ComputeBonus(int balance, double pct, int max, int expected) =>
        Assert.Equal(expected, DailyBonusMath.ComputeBonus(balance, pct, max));
}

using CasinoShiz.Configuration;
using CasinoShiz.Data.Entities;
using CasinoShiz.Services.Dice;
using Xunit;

namespace CasinoShiz.Tests;

public class DicePrizeTests
{
    [Theory]
    [InlineData(1,  new[] { 0, 0, 0 })]
    [InlineData(22, new[] { 1, 1, 1 })]
    [InlineData(43, new[] { 2, 2, 2 })]
    [InlineData(64, new[] { 3, 3, 3 })]
    public void DecodeRolls_ReturnsExpectedSymbols(int diceValue, int[] expected)
    {
        var rolls = InvokePrivateDecode(diceValue);
        Assert.Equal(expected, rolls);
    }

    [Fact]
    public void GetMoreRollsAvailable_CurrentDay_ReturnsRemaining()
    {
        var user = new UserState
        {
            LastDayUtc = CasinoShiz.Helpers.TimeHelper.GetCurrentDayMillis(),
            AttemptCount = 1,
            ExtraAttempts = 0,
        };
        Assert.Equal(2, DiceService.GetMoreRollsAvailable(user, attemptsLimit: 3));
    }

    [Fact]
    public void GetMoreRollsAvailable_NewDay_ReturnsFullLimit()
    {
        var user = new UserState
        {
            LastDayUtc = 0,
            AttemptCount = 10,
            ExtraAttempts = 5,
        };
        Assert.Equal(3, DiceService.GetMoreRollsAvailable(user, attemptsLimit: 3));
    }

    [Fact]
    public void GetMoreRollsAvailable_Exhausted_ReturnsZero()
    {
        var user = new UserState
        {
            LastDayUtc = CasinoShiz.Helpers.TimeHelper.GetCurrentDayMillis(),
            AttemptCount = 3,
            ExtraAttempts = 0,
        };
        Assert.Equal(0, DiceService.GetMoreRollsAvailable(user, attemptsLimit: 3));
    }

    [Fact]
    public void GetMoreRollsAvailable_WithExtras_ReturnsExtrasPlusRemaining()
    {
        var user = new UserState
        {
            LastDayUtc = CasinoShiz.Helpers.TimeHelper.GetCurrentDayMillis(),
            AttemptCount = 3,
            ExtraAttempts = 2,
        };
        Assert.Equal(2, DiceService.GetMoreRollsAvailable(user, attemptsLimit: 3));
    }

    private static int[] InvokePrivateDecode(int diceValue)
    {
        var method = typeof(DiceService).GetMethod(
            "DecodeRolls",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);
        return (int[])method!.Invoke(null, [diceValue])!;
    }
}

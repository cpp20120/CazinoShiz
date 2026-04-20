using Games.Horse;
using Xunit;

namespace CasinoShiz.Tests;

public class HorseTimeHelperTests
{
    [Fact]
    public void GetRaceDate_ReturnsMMDDYYYYFormat()
    {
        var date = HorseTimeHelper.GetRaceDate();
        Assert.Matches(@"^\d{2}-\d{2}-\d{4}$", date);
    }

    [Fact]
    public void GetRaceDate_IsConsistentAcrossConsecutiveCalls()
    {
        var first = HorseTimeHelper.GetRaceDate();
        var second = HorseTimeHelper.GetRaceDate();
        Assert.Equal(first, second);
    }

    [Fact]
    public void GetRaceDate_MonthAndDayAreValid()
    {
        var date = HorseTimeHelper.GetRaceDate();
        var parts = date.Split('-');
        var month = int.Parse(parts[0]);
        var day = int.Parse(parts[1]);
        Assert.InRange(month, 1, 12);
        Assert.InRange(day, 1, 31);
    }

    [Fact]
    public void GetRaceDate_YearIsReasonable()
    {
        var date = HorseTimeHelper.GetRaceDate();
        var parts = date.Split('-');
        var year = int.Parse(parts[2]);
        Assert.InRange(year, 2020, 2100);
    }
}

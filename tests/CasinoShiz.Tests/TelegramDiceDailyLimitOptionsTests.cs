using System.Collections.Generic;
using BotFramework.Host.Composition;
using Xunit;

namespace CasinoShiz.Tests;

public class TelegramDiceDailyLimitOptionsTests
{
    [Fact]
    public void GetMaxRollsPerUserPerDay_UsesPerGameLimit()
    {
        var options = new TelegramDiceDailyLimitOptions
        {
            MaxRollsPerUserPerDay = 5,
            MaxRollsPerUserPerDayByGame = new Dictionary<string, int>
            {
                ["dice"] = 2,
                ["basketball"] = 8,
            },
        };

        Assert.Equal(2, options.GetMaxRollsPerUserPerDay("dice"));
        Assert.Equal(8, options.GetMaxRollsPerUserPerDay("basketball"));
    }

    [Fact]
    public void GetMaxRollsPerUserPerDay_FallsBackToSharedLimit()
    {
        var options = new TelegramDiceDailyLimitOptions
        {
            MaxRollsPerUserPerDay = 5,
        };

        Assert.Equal(5, options.GetMaxRollsPerUserPerDay("darts"));
    }
}

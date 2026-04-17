using CasinoShiz.Helpers;
using Xunit;

namespace BusinoBot.Tests;

public class RussianPluralTests
{
    private static readonly string[] Coins = ["монета", "монеты", "монет"];

    [Theory]
    [InlineData(1, "монета")]
    [InlineData(2, "монеты")]
    [InlineData(5, "монет")]
    [InlineData(11, "монет")]
    [InlineData(21, "монета")]
    [InlineData(22, "монеты")]
    [InlineData(25, "монет")]
    [InlineData(101, "монета")]
    [InlineData(112, "монет")]
    [InlineData(0, "монет")]
    public void Plural_PicksCorrectForm(int n, string expected)
    {
        Assert.Equal(expected, RussianPlural.Plural(n, Coins));
    }

    [Fact]
    public void Plural_IncludeNumber_PrefixesNumber()
    {
        Assert.Equal("3 монеты", RussianPlural.Plural(3, Coins, includeNumber: true));
    }
}

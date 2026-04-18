using CasinoShiz.Services;
using Xunit;

namespace CasinoShiz.Tests;

public class TaxServiceTests
{
    [Fact]
    public void GasTax_ZeroVolume_ReturnsOne()
    {
        Assert.Equal(1, TaxService.GetGasTax(0));
    }

    [Fact]
    public void GasTax_LargeVolume_UsesPercentage()
    {
        var tax = TaxService.GetGasTax(1000);
        Assert.True(tax >= 35 && tax <= 45, $"expected ~40, got {tax}");
    }

    [Fact]
    public void BankTax_LowBalance_Negative()
    {
        Assert.Equal(-2, TaxService.GetBankTax(50));
    }

    [Fact]
    public void BankTax_MidBalance_Zero()
    {
        Assert.Equal(0, TaxService.GetBankTax(100));
    }

    [Fact]
    public void BankTax_HighBalance_Positive()
    {
        Assert.True(TaxService.GetBankTax(10_000) > 0);
    }

    [Fact]
    public void BankTax_CappedAtTwelveOrThirteen()
    {
        Assert.True(TaxService.GetBankTax(1_000_000) <= 13);
    }
}

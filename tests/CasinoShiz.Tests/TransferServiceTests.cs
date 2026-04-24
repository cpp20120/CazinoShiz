using BotFramework.Host;
using Games.Transfer;
using Xunit;

namespace CasinoShiz.Tests;

public sealed class TransferServiceTests
{
    [Theory]
    [InlineData(100, 3, 103)]
    [InlineData(1, 1, 2)]
    [InlineData(200, 6, 206)]
    [InlineData(33, 1, 34)]
    [InlineData(42, 2, 44)]
    public void Fee_RoundsPercentToHalfThenWhole_WithMinOne(int net, int expectedFee, int expectedTotal)
    {
        const int minFee = 1;
        var fee = TransferOptions.ComputeFeeCoins(net, 0.03, minFee);
        var total = TransferOptions.ComputeTotalDebit(net, 0.03, minFee);
        Assert.Equal(expectedFee, fee);
        Assert.Equal(expectedTotal, total);
    }

    [Fact]
    public async Task TryTransferAsync_CreditsRecipientNetAndBurnsFee()
    {
        var economics = new FakeEconomicsService { StartingBalance = 500 };
        var svc = new TransferService(
            economics,
            new NullAnalyticsService(),
            new FakeRuntimeTuning { Transfer = new TransferOptions { FeePercent = 0.03, MinFeeCoins = 1 } });

        var r = await svc.TryTransferAsync(1, 2, chatId: -100, "a", "b", netToRecipient: 100, default);

        Assert.Equal(TransferError.None, r.Error);
        Assert.Equal(100, r.NetToRecipient);
        Assert.Equal(3, r.FeeCoins);
        Assert.Equal(103, r.TotalDebited);
        Assert.Equal(397, r.SenderBalance);
        Assert.Equal(600, r.RecipientBalance);

        Assert.Single(economics.Debits);
        Assert.Equal(103, economics.Debits[0].Amount);
        Assert.Single(economics.Credits);
        Assert.Equal(100, economics.Credits[0].Amount);
    }
}

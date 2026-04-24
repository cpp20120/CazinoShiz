namespace Games.Transfer;

public sealed class TransferOptions
{
    public const string SectionName = "Games:transfer";

    /// <summary>Fee rate on the amount the recipient receives (e.g. 0.03 = 3%).</summary>
    public double FeePercent { get; init; } = 0.03;

    /// <summary>Minimum fee in whole coins after rounding.</summary>
    public int MinFeeCoins { get; init; } = 1;

    public int MinNetCoins { get; init; } = 1;

    /// <summary>0 = no cap.</summary>
    public int MaxNetCoins { get; init; } = 0;

    /// <summary>
    /// Fee = <paramref name="netToRecipient"/> × <paramref name="feePercent"/>, rounded to the nearest
    /// <b>0.5</b>, then to a whole coin (half values round away from zero), then at least
    /// <paramref name="minFeeCoins"/>.
    /// </summary>
    public static int ComputeFeeCoins(int netToRecipient, double feePercent, int minFeeCoins)
    {
        var raw = netToRecipient * feePercent;
        var toHalf = Math.Round(raw * 2, MidpointRounding.AwayFromZero) / 2.0;
        var whole = (int)Math.Round(toHalf, MidpointRounding.AwayFromZero);
        return Math.Max(minFeeCoins, whole);
    }

    public static int ComputeTotalDebit(int netToRecipient, double feePercent, int minFeeCoins) =>
        netToRecipient + ComputeFeeCoins(netToRecipient, feePercent, minFeeCoins);
}

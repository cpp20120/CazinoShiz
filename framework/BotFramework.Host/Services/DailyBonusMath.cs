namespace BotFramework.Host.Services;

public static class DailyBonusMath
{
    /// <summary>Integer coins from floor(<paramref name="balance"/> × <paramref name="percentOfBalance"/> / 100), capped. Returns 0 if floored value is 0 (don't grant dust).</summary>
    public static int ComputeBonus(int balance, double percentOfBalance, int maxBonus)
    {
        if (balance <= 0 || maxBonus <= 0) return 0;
        if (percentOfBalance <= 0) return 0;
        var raw = Math.Floor(balance * (percentOfBalance / 100.0));
        if (raw < 1) return 0;
        return (int)Math.Min(maxBonus, raw);
    }
}

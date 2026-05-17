namespace BotFramework.Host.Composition;

public sealed class DailyBonusOptions
{
    public const string SectionName = "Bot:DailyBonus";

    public bool Enabled { get; set; } = true;

    /// <summary>Percent of <b>current balance</b> to credit once per local day, e.g. 0.4 means 0.4% (not 40%). Capped by <see cref="MaxBonus"/>.</summary>
    public double PercentOfBalance { get; set; } = 0.35;

    /// <summary>Upper cap so large balances do not get a "prize-sized" handout.</summary>
    public int MaxBonus { get; set; } = 8;

    /// <summary>Hours east of UTC for the calendar day boundary (7 = same day as /horse, UTC+7).</summary>
    public int TimezoneOffsetHours { get; set; } = 7;

    /// <summary>On startup, backfill missed past local days for wallets that have claimed before.</summary>
    public bool CatchUpEnabled { get; set; } = true;

    /// <summary>Safety cap for startup catch-up, so an old/null date cannot create an unbounded payout loop.</summary>
    public int MaxCatchUpDays { get; set; } = 14;
}
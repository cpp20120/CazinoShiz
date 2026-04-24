namespace BotFramework.Host.Composition;

/// <summary>Shared daily cap for user-initiated Telegram random-dice games (🎰 🎲 🎯 🎳 🏀 ⚽) per wallet.</summary>
public sealed class TelegramDiceDailyLimitOptions
{
    public const string SectionName = "Bot:TelegramDiceDailyLimit";

    /// <summary>0 = unlimited.</summary>
    public int MaxRollsPerUserPerDay { get; set; } = 0;

    /// <summary>Same convention as <see cref="DailyBonusOptions.TimezoneOffsetHours"/> (hours east of UTC).</summary>
    public int TimezoneOffsetHours { get; set; } = 7;
}

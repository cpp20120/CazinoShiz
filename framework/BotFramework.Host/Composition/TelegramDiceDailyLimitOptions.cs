using System;
using System.Collections.Generic;
using System.Linq;

namespace BotFramework.Host.Composition;

/// <summary>Daily caps for user-initiated Telegram random-dice games (🎰 🎲 🎯 🎳 🏀 ⚽) per wallet.</summary>
public sealed class TelegramDiceDailyLimitOptions
{
    public const string SectionName = "Bot:TelegramDiceDailyLimit";

    /// <summary>Fallback for games not present in <see cref="MaxRollsPerUserPerDayByGame"/>. 0 = unlimited.</summary>
    public int MaxRollsPerUserPerDay { get; set; } = 0;

    /// <summary>Per-game caps keyed by stable game id. 0 = unlimited for that game.</summary>
    public Dictionary<string, int> MaxRollsPerUserPerDayByGame { get; set; } = new();

    /// <summary>Same convention as <see cref="DailyBonusOptions.TimezoneOffsetHours"/> (hours east of UTC).</summary>
    public int TimezoneOffsetHours { get; set; } = 7;

    public int GetMaxRollsPerUserPerDay(string gameId)
    {
        if (MaxRollsPerUserPerDayByGame.TryGetValue(gameId, out var max))
            return max;

        var match = MaxRollsPerUserPerDayByGame.FirstOrDefault(
            kv => string.Equals(kv.Key, gameId, StringComparison.OrdinalIgnoreCase));

        return string.IsNullOrWhiteSpace(match.Key)
            ? MaxRollsPerUserPerDay
            : match.Value;
    }
}

namespace Games.Horse;

public static class HorseTimeHelper
{
    /// <summary>Calendar day key for the horse pool, using <paramref name="timezoneOffsetHours"/> hours east of UTC (same convention as bot daily bonus).</summary>
    public static string GetRaceDate(int timezoneOffsetHours = 7)
    {
        var offset = TimeSpan.FromHours(timezoneOffsetHours);
        var now = DateTimeOffset.UtcNow.ToOffset(offset);
        var day = new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, offset);
        return day.ToString("MM-dd-yyyy");
    }
}

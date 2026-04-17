namespace CasinoShiz.Helpers;

public static class TimeHelper
{
    private static readonly TimeSpan Utc7Offset = TimeSpan.FromHours(7);

    public static DateTimeOffset GetCurrentDay()
    {
        var now = DateTimeOffset.UtcNow.ToOffset(Utc7Offset);
        return new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, Utc7Offset);
    }

    public static long GetCurrentDayMillis() => GetCurrentDay().ToUnixTimeMilliseconds();

    public static int GetDaysBetween(DateTimeOffset current, DateTimeOffset last)
    {
        return (int)Math.Floor((current - last).TotalDays);
    }

    public static DateTimeOffset GetDateFromMillis(long millis)
    {
        return DateTimeOffset.FromUnixTimeMilliseconds(millis).ToOffset(Utc7Offset);
    }

    public static string GetRaceDate()
    {
        var day = GetCurrentDay();
        return day.ToString("MM-dd-yyyy");
    }
}

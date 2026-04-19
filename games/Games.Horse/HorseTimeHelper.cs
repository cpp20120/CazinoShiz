namespace Games.Horse;

public static class HorseTimeHelper
{
    private static readonly TimeSpan Utc7Offset = TimeSpan.FromHours(7);

    public static string GetRaceDate()
    {
        var now = DateTimeOffset.UtcNow.ToOffset(Utc7Offset);
        var day = new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, Utc7Offset);
        return day.ToString("MM-dd-yyyy");
    }
}

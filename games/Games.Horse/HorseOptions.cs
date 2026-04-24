namespace Games.Horse;

public sealed class HorseOptions
{
    public const string SectionName = "Games:horse";

    public int HorseCount { get; init; } = 4;
    public int MinBetsToRun { get; init; } = 4;
    public int AnnounceDelayMs { get; init; } = 20_000;

    /// <summary>Hours east of UTC for race calendar day and for interpreting <see cref="AutoRunLocalHour"/> / <see cref="AutoRunLocalMinute"/>.</summary>
    public int TimezoneOffsetHours { get; init; } = 7;

    /// <summary>When true, a background job runs one global race per calendar day after the configured local time (if <see cref="MinBetsToRun"/> is met).</summary>
    public bool AutoRunEnabled { get; init; }

    /// <summary>Local wall-clock hour (0–23) in <see cref="TimezoneOffsetHours"/> after which the auto-run may fire.</summary>
    public int AutoRunLocalHour { get; init; } = 21;

    /// <summary>Local wall-clock minute (0–59).</summary>
    public int AutoRunLocalMinute { get; init; }

    public List<long> Admins { get; init; } = [];
}

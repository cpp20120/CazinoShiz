namespace Games.Meta;

public sealed class MetaOptions
{
    public const string SectionName = "Games:meta";

    public long HighRollerTotalStaked { get; set; } = 1_000;
    public long BigPayoutMinimum { get; set; } = 1_000;
    public int StreakTimezoneOffsetHours { get; set; } = 7;
}
